using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>Mod 正显示的这一关：编辑本关手序时交给编辑器的东西。</summary>
internal sealed class LevelSnapshot
{
    public LevelSnapshot(List<TileHit> hits, FingeringResult fingering, LevelInfo info, List<BeatLine> beats, List<(double, double)> tempo, int tier)
    {
        Tier = tier;
        Hits = hits;
        Fingering = fingering;
        Info = info;
        Beats = beats;
        Tempo = tempo;
    }

    public List<TileHit> Hits { get; }

    /// <summary>估算难度档位（1–60），0 表示估不出。</summary>
    public int Tier { get; }

    public FingeringResult Fingering { get; }

    public LevelInfo Info { get; }

    public List<BeatLine> Beats { get; }

    /// <summary>BPM 变化点：时间、BPM。</summary>
    public List<(double Time, double Bpm)> Tempo { get; }
}

/// <summary>
/// 「编辑本关手序」一条龙：把本关的砖块、正在用的手序、两种自动排法、节拍线、轨道形状、音乐和波形写进编辑器目录，
/// 再用浏览器打开编辑器（Mods/RhythmHint/Editor/index.html），编辑器自动载入。保存后的文件放进手序文件夹，关卡里马上换上。
/// </summary>
internal static class EditorLauncher
{
    /// <summary>波形每秒多少个点。</summary>
    private const int PeaksPerSecond = 100;

    /// <summary>从游戏里读音乐时每次读多少帧（分块读，不一次占几十 MB 内存）。</summary>
    private const int ChunkFrames = 1 << 16;

    public static string Folder => Path.Combine(Main.Mod.Path, "Editor");

    /// <summary>
    /// AudioClip.GetData(float[], int)。Unity 6 另有 Span&lt;float&gt; 重载，直接调用时编译器要解析 Span，
    /// 而 Mod 编译用的 .NET Framework 4.8 参考程序集里没有它（CS7069）；按参数类型取出 float[] 版本做成委托，不经过重载解析。
    /// </summary>
    private static readonly Func<AudioClip, float[], int, bool>? GetData = typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) })
        is { } method
        ? (Func<AudioClip, float[], int, bool>)Delegate.CreateDelegate(typeof(Func<AudioClip, float[], int, bool>), method)
        : null;

    /// <returns>给玩家看的结果。</returns>
    public static (Message Text, bool Ok) Open(LevelSnapshot snapshot, LevelState level, FingeringSource source)
    {
        var page = Path.Combine(Folder, "index.html");
        if (!File.Exists(page))
        {
            return (Lang.M("找不到手序编辑器（{0}），请重新安装 Mod。", page), false);
        }

        var watch = Stopwatch.StartNew();
        var settings = Main.Settings;
        // 编辑器按这一关显示的键数打开（6K、8K 的轨道写进 4 键文件会被拒绝）；另外两种键数一起带上，编辑器里能切换（2026-11-22 用户）
        // 双手稳拍（试验性）参考拍线和进砖方向：游戏里用的是它时 snapshot.Beats 就是它的拍线，否则另推一份给编辑器的参考栏
        var path = GameReader.ReadPath(level, snapshot.Hits);
        var start = level.Floors[0].entryTime;
        var tempo = snapshot.Tempo.ConvertAll(t => (t.Time, 60 / t.Bpm));
        var steadyBeats = snapshot.Beats;
        if (settings.ActiveStyle != FingeringStyle.SteadyHands && snapshot.Hits.Count > 0)
        {
            try
            {
                steadyBeats = BeatGrid.InferForSteadyHands(snapshot.Hits, GameReader.ReadSpeeds(level),
                    GameReader.Crotchet(level), start, snapshot.Hits[snapshot.Hits.Count - 1].Time + 2).Lines;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArithmeticException or IndexOutOfRangeException)
            {
                Main.Mod.Logger.Warning("编辑器：双手稳拍推算拍线失败，沿用当前拍线。" + ex.Message);
            }
        }

        var rhythm = new FingeringRhythm(start, tempo, FingeringRhythm.IncomingDirections(path), steadyBeats);
        var main = Keyed(snapshot, source, snapshot.Fingering, rhythm);
        List<PlannedNote> AutoFor(int keys) => NotePlanner.Plan(snapshot.Hits, settings.ToFingering(keys), rhythm);
        var variants = new List<SessionVariant>();
        foreach (var keys in KeyLayout.Modes)
        {
            if (keys != main.Fingering.Keys)
            {
                variants.Add(Keyed(snapshot, source, source.ChooseFor(snapshot.Hits, AutoFor, settings, keys), rhythm));
            }
        }

        var id = main.Id;
        var session = new EditorSession
        {
            Id = id,
            Fingering = main.Fingering,
            FromAuto = main.FromAuto,
            UsedFile = main.UsedFile,
            FileName = main.FileName,
            SaveFolder = FingeringSource.Inbox ?? FingeringSource.Folder,
            SaveStartIn = FingeringSource.Inbox != null ? "documents" : string.Empty,
            Language = Lang.Current,
            ModVersion = Main.Mod.Info.Version,
            RoundNotes = settings.RoundNotes,
            KeyLabels = main.KeyLabels,
            Standard = main.Standard,
            Alternate = main.Alternate,
            Steady = main.Steady,
            Variants = variants,
            Beats = snapshot.Beats,
            Tempo = snapshot.Tempo,
            Path = path,
            Audio = ExportAudio(level, snapshot.Info, id),
            HitSound = ExportHitSound(level),
            Effects = ReadEffects(level),
        };

        Directory.CreateDirectory(FingeringSource.Folder);
        if (FingeringSource.Inbox != null)
        {
            Directory.CreateDirectory(FingeringSource.Inbox); // 先建好，玩家在浏览器里选得到
        }

        SessionLibrary.Save(Folder, session, DateTimeOffset.Now); // session.js + 编辑器的历史（recent/）
        var audioFile = session.Audio.File;
        Main.Mod.Logger.Log($"手序编辑器数据已写好（{watch.ElapsedMilliseconds} ms，音乐：{(audioFile.Length == 0 ? "无" : audioFile)}）");
        OpenPage(page);
        return (audioFile.Length == 0
            ? Lang.M("已在浏览器中打开手序编辑器。没能带上这首歌的音乐，可以在编辑器里手动选择。")
            : Lang.M("已在浏览器中打开手序编辑器。"), true);
    }

    /// <summary>
    /// 一种键数要交给编辑器的：这个键数的手序（专属手序或自动排键）、文件名、键位、几种自动排法。
    /// 用上的手序键数和这一关显示的不一样（4K 手序放进 6K 里显示）时，编辑器另存一份这个键数的，不覆盖原来那份。
    /// </summary>
    private static SessionVariant Keyed(LevelSnapshot snapshot, FingeringSource source, FingeringResult result, FingeringRhythm rhythm)
    {
        var settings = Main.Settings;
        var info = snapshot.Info;
        var keys = result.Keys;
        var fingering = CustomFingering.From(snapshot.Hits, result.Notes, settings.LeftHanded, keys);
        fingering.Song = info.Song;
        fingering.Artist = info.Artist;
        fingering.Charter = info.Charter;
        var used = result.UsedFile.Length > 0 ? source.Get(result.UsedFile) : null;
        if (used != null)
        {
            // 作者、说明，旧版的谱面指纹（作者为更新版重新保存时，手序同时认得旧版和这一版），以后的版本加的字段（编辑器原样写回）
            fingering.TakeDetailsFrom(used);
        }

        var labels = settings.KeyLabelsFor(keys).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var usedFile = used != null && used.Keys == keys ? result.UsedFile : string.Empty;
        return new SessionVariant
        {
            Id = SessionLibrary.IdOf(fingering),
            Fingering = fingering,
            FromAuto = result.FromAuto,
            UsedFile = usedFile,
            FileName = usedFile.Length > 0 ? usedFile : fingering.SuggestedFileName(),
            KeyLabels = labels.Length == keys ? labels : KeyBindings.Split(new Settings().KeyLabelsFor(keys), keys),
            Standard = NotePlanner.Plan(snapshot.Hits, WithStyle(FingeringStyle.TimeReset, keys)),
            Alternate = NotePlanner.Plan(snapshot.Hits, WithStyle(FingeringStyle.ReviewPreferenceV1, keys)),
            Steady = NotePlanner.Plan(snapshot.Hits, WithStyle(FingeringStyle.SteadyHands, keys), rhythm),
        };
    }

    /// <summary>让轨道和镜头动起来的关卡事件（轨道预览按时间重放）；读不出来为 null，预览就是静止的轨道。</summary>
    private static JObject? ReadEffects(LevelState level)
    {
        var data = GameReader.LevelData();
        if (data == null)
        {
            Main.Mod.Logger.Log("编辑器：这一关没有关卡数据，轨道预览不带动画");
            return null;
        }

        try
        {
            var effects = TrackEffects.Extract(JObject.Parse(data.Encode()));
            TrackEffects.AddBeats(effects, GameReader.ReadSpeeds(level), GameReader.Crotchet(level));
            Main.Mod.Logger.Log($"编辑器：轨道预览带上 {((JArray)effects["actions"]!).Count} 个轨道和镜头事件");
            return effects;
        }
        catch (JsonReaderException ex)
        {
            // 游戏自己写出来的关卡读不了（不该发生）：轨道预览不带动画，别的照常
            Main.Mod.Logger.Warning($"编辑器：读关卡事件失败，轨道预览不带动画：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 在桌面放一个打开编辑器的快捷方式（2026-11-18 用户：改到一半关了网页，不开游戏很难再找到它）。
    /// 写成 Windows 快捷方式（.lnk，见 ShellLink），用编辑器自己的图标（Editor/icon.ico），双击用默认浏览器打开编辑器；
    /// 编辑器先显示最近一关，左边「历史」里能换别的关卡接着改。
    /// </summary>
    public static (Message Text, bool Ok) CreateShortcut()
    {
        var page = Path.GetFullPath(Path.Combine(Folder, "index.html"));
        if (!File.Exists(page))
        {
            return (Lang.M("找不到手序编辑器（{0}），请重新安装 Mod。", page), false);
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (desktop.Length == 0 || !Directory.Exists(desktop))
        {
            return (Lang.M("找不到桌面文件夹，无法创建快捷方式。"), false);
        }

        var title = Lang.T("RhythmHint 手序编辑器");
        var icon = Path.GetFullPath(Path.Combine(Folder, "icon.ico"));
        var path = Path.Combine(desktop, string.Join("_", title.Split(Path.GetInvalidFileNameChars())) + ".lnk");
        File.WriteAllBytes(path, ShellLink.Build(page, Path.GetFullPath(Folder), File.Exists(icon) ? icon : null, title));
        Main.Mod.Logger.Log($"编辑器：桌面快捷方式 {path}");
        return (Lang.M("已在桌面创建手序编辑器的快捷方式。"), true);
    }

    /// <summary>打开手序文件夹（没有就先建）。</summary>
    public static void OpenFolder()
    {
        Directory.CreateDirectory(FingeringSource.Folder);
        OpenPage(FingeringSource.Folder);
    }

    /// <summary>玩家现在的排键设置，只换排法（编辑器里两种排法对照）。</summary>
    private static FingeringOptions WithStyle(FingeringStyle style, int keys)
    {
        var options = Main.Settings.ToFingering(keys);
        options.Style = style;
        return options;
    }

    /// <summary>
    /// 音乐文件里的位置 = 谱面时间 + 这个数 = 关卡 offset − 起始一拍 × 倒计时拍数（MusicTiming：游戏的砖块时间里
    /// 算进去了「倒计时拍数 − 1」拍）。倒计时拍数直接读游戏算好的 adjustedCountdownTicks（倒计时拍数 ÷ 倒计时变速倍率）。
    /// 音乐在放时顺便量一下（音乐放到哪 − 谱面时间），只写日志对照，不参与计算。
    /// </summary>
    private static double MusicOffset(LevelState level, LevelInfo info)
    {
        var crotchet = GameReader.Crotchet(level);
        var ticks = level.Conductor.adjustedCountdownTicks;
        var offset = MusicTiming.Offset(info.Offset, crotchet, ticks - 1);
        Main.Mod.Logger.Log($"编辑器：偏移 {offset:F3} 秒（关卡 offset {info.Offset:F3}、一拍 {crotchet:F4}、倒计时 {ticks:0.##} 拍）");
        var song = level.Conductor.song;
        if (!level.Failed && song != null && song.clip != null && song.isPlaying && song.time >= 0.05f && song.time <= song.clip.length - 0.05f)
        {
            var measured = song.time - level.Conductor.songposition_minusi;
            Main.Mod.Logger.Log($"编辑器：对照：此刻音乐 {song.time:F3} 秒、谱面 {level.Conductor.songposition_minusi:F3} 秒，差 {measured:F3}（和偏移差 {(measured - offset) * 1000:F0} 毫秒）");
        }

        return offset;
    }

    /// <summary>
    /// 音乐：自定义关卡拷贝歌曲文件；官方关卡把游戏里正在播的音频写成 WAV。波形从游戏里的音频算（读不了就没有波形）。
    /// 音乐放在编辑器的历史（recent/）里，跟着这一关留着；读不到音乐时保留偏移和校准，编辑器手动选歌仍能对齐。
    /// </summary>
    private static SessionAudio ExportAudio(LevelState level, LevelInfo info, string id)
    {
        var clip = level.Conductor.song != null ? level.Conductor.song.clip : null;

        // 还没按键开始时游戏还没载入音乐（游戏自带关卡没有歌曲文件，只能从这里读）：临时载入，读完卸掉还原；开始时游戏本来也会载入它
        var loadedHere = clip != null && clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Unloaded
            && clip.LoadAudioData() && clip.loadState == AudioDataLoadState.Loaded;
        try
        {
            return ExportAudio(level, info, id, clip);
        }
        finally
        {
            if (loadedHere)
            {
                clip!.UnloadAudioData();
            }
        }
    }

    private static SessionAudio ExportAudio(LevelState level, LevelInfo info, string id, AudioClip? clip)
    {
        var readable = clip != null && clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Loaded;
        // 导出谱面规定的音高。游戏另乘飙速/试玩倍率，编辑器有自己的练习速度。
        var pitch = GameReader.LevelData()?.pitch ?? 100;
        var audio = new SessionAudio { Offset = MusicOffset(level, info), Pitch = pitch > 0 ? pitch / 100.0 : 1, Calibration = scrConductor.calibration_v };
        Main.Mod.Logger.Log($"编辑器：游戏的画面校准 {audio.Calibration * 1000:F0} 毫秒，编辑器照样补");
        try
        {
            if (info.SongFile.Length > 0 && File.Exists(info.SongFile))
            {
                audio.File = SessionLibrary.AudioFile(Folder, id, Path.GetExtension(info.SongFile));
                File.Copy(info.SongFile, Path.Combine(Folder, audio.File), overwrite: true);
                WriteAudioScript(info.SongFile, audio);
                var hasPeaks = readable && TryDump(clip!, null, audio);
                if (!hasPeaks && string.Equals(Path.GetExtension(info.SongFile), ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (!Mp3Waveform.TryRead(info.SongFile, audio, PeaksPerSecond))
                        {
                            Main.Mod.Logger.Warning("编辑器：MP3 文件没有可解码的采样，已保留歌曲供试听");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 文件已复制，解码失败只影响波形；MP3Sharp 对坏帧也可能抛出普通运行时异常。
                        Main.Mod.Logger.Warning($"编辑器：MP3 波形读取失败：{ex.Message}");
                    }
                }

                return audio;
            }

            if (!readable)
            {
                Main.Mod.Logger.Log(clip == null ? "编辑器：本关没有音频" : $"编辑器：游戏里的音频读不出来（{clip.loadType}，{clip.loadState}）");
                return audio;
            }

            audio.File = SessionLibrary.AudioFile(Folder, id, ".wav");
            using (var stream = new FileStream(Path.Combine(Folder, audio.File), FileMode.Create, FileAccess.ReadWrite))
            {
                if (TryDump(clip!, stream, audio))
                {
                    return audio;
                }
            }

            File.Delete(Path.Combine(Folder, audio.File));
            audio.File = string.Empty;
            return audio;
        }
        catch (IOException ex)
        {
            // 磁盘满、文件被占用：不带音乐照样打开编辑器
            Main.Mod.Logger.Warning($"编辑器：导出音乐失败：{ex.Message}");
            audio.File = string.Empty;
            return audio;
        }
    }

    /// <summary>
    /// 编辑器的打拍声用这一关游戏里实际响的那个打击声（scrConductor.hitSound，音频名是 "snd" + 名字，
    /// 默认的是 Kick → sndKick）；这一关没有打击声时用默认的 sndKick。游戏开始后才载入打击声，只读已经载入的，
    /// 不替游戏加载；还没开始时用打歌时存下的那份（见 RememberHitSound）。都没有返回空，编辑器用合成的声音代替。
    /// </summary>
    private static byte[] ExportHitSound(LevelState level)
    {
        var current = level.Conductor.hitSound;
        var names = new List<string> { HitSoundName(current) };
        if (!names.Contains(DefaultHitSound))
        {
            names.Add(DefaultHitSound);
        }

        foreach (var name in names)
        {
            var wav = ReadHitSound(name, searchAll: true);
            if (wav.Length > 0)
            {
                Main.Mod.Logger.Log($"编辑器：打拍声用游戏的 {name}（这一关的打击声是 {current}，{wav.Length} 字节）");
                SaveHitSound(name, wav);
                return wav;
            }

            var cached = HitSoundCache(name);
            if (File.Exists(cached))
            {
                try
                {
                    var saved = File.ReadAllBytes(cached);
                    Main.Mod.Logger.Log($"编辑器：游戏还没载入打击声 {name}，用打歌时存下的那份");
                    return saved;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 读不了存下的打击声只是用合成的声音，编辑器照样打开
                    Main.Mod.Logger.Warning($"编辑器：读存下的打击声 {name} 失败：{ex.Message}");
                }
            }
        }

        Main.Mod.Logger.Log($"编辑器：读不到游戏的打击声（{string.Join("、", names)} 都没载入，也没存过），打拍声用合成的；开始打歌后再打开编辑器就是原声");
        return Array.Empty<byte>();
    }

    private const string DefaultHitSound = "sndKick";

    /// <summary>这一种打击声已经存过（或存不了），这次运行游戏不再试。</summary>
    private static readonly HashSet<string> SavedHitSounds = new();

    private static float _nextHitSoundTry;

    /// <summary>读失败过的打击声和那一关的指纹：同一关、同一种打击声不再重试（每次重试都要新分配约 350 KB 的采样数组）。</summary>
    private static (string Name, (int, int, int, double, bool) Level)? _hitSoundFailed;

    /// <summary>
    /// 打歌时调用（每秒最多真做一次）：游戏已经载入这一关的打击声、还没存过时，写成 Editor/hitsound-名字.wav。
    /// 还没按键开始时游戏没载入打击声，那时打开编辑器就用存下的这份。每种打击声只存一次；
    /// 读失败了这一关不再重试，换关卡或这一关换了打击声再试。
    /// </summary>
    public static void RememberHitSound(LevelState level)
    {
        if (Time.unscaledTime < _nextHitSoundTry)
        {
            return;
        }

        _nextHitSoundTry = Time.unscaledTime + 1;
        var name = HitSoundName(level.Conductor.hitSound);
        if (SavedHitSounds.Contains(name))
        {
            return;
        }

        if (File.Exists(HitSoundCache(name)))
        {
            SavedHitSounds.Add(name);
            return;
        }

        var print = GameReader.Fingerprint(level);
        if (_hitSoundFailed is { } failed && failed.Name == name && failed.Level == print)
        {
            return;
        }

        // 打歌中只查 AudioManager.audioLib（字典），不在内存里翻所有音频，免得卡顿
        var wav = ReadHitSound(name, searchAll: false, out var tried);
        if (wav.Length > 0)
        {
            SaveHitSound(name, wav);
        }
        else if (tried)
        {
            _hitSoundFailed = (name, print);
            Main.Mod.Logger.Warning($"编辑器：读游戏的打击声 {name} 失败，这一关不再重试（打开编辑器时打拍声用合成的）");
        }
    }

    private static string HitSoundName(HitSound sound) => sound == HitSound.None ? DefaultHitSound : "snd" + sound;

    private static string HitSoundCache(string name) => Path.Combine(Folder, "hitsound-" + name + ".wav");

    private static void SaveHitSound(string name, byte[] wav)
    {
        SavedHitSounds.Add(name);
        var path = HitSoundCache(name);
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(path, wav);
            Main.Mod.Logger.Log($"编辑器：存下打击声 {name}（{wav.Length} 字节），还没开始时打开编辑器也能用");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 存不了只是下次还没开始时打开编辑器用合成的声音，不影响别的
            Main.Mod.Logger.Warning($"编辑器：存打击声 {name} 失败：{ex.Message}");
        }
    }

    /// <summary>从游戏已经载入的音频读打击声（最长一秒的 WAV）；没载入或读不出来为空。</summary>
    /// <param name="searchAll">audioLib 里没有时再按名字找内存里所有的 AudioClip（慢，打歌中不用）。</param>
    private static byte[] ReadHitSound(string name, bool searchAll) => ReadHitSound(name, searchAll, out _);

    /// <param name="tried">找到了已经载入的音频、真的读过一遍（读失败也是 true；没找到、没载入为 false，过一会儿可能就有了）。</param>
    private static byte[] ReadHitSound(string name, bool searchAll, out bool tried)
    {
        tried = false;
        var clip = FindLoadedClip(name, searchAll);
        if (clip == null || GetData == null || clip.loadState != AudioDataLoadState.Loaded)
        {
            return Array.Empty<byte>();
        }

        tried = true;
        var frames = Math.Min(clip.samples, clip.frequency);
        var samples = new float[frames * clip.channels];
        return GetData(clip, samples, 0) ? AudioDump.ShortWav(samples, clip.channels, clip.frequency, maxSeconds: 1) : Array.Empty<byte>();
    }

    /// <summary>游戏已经载入的音频：先查 AudioManager.audioLib，再按名字找内存里的 AudioClip。</summary>
    private static AudioClip? FindLoadedClip(string name, bool searchAll)
    {
        var manager = AudioManager.Instance;
        if (manager != null && manager.audioLib != null && manager.audioLib.TryGetValue(name, out var clip) && clip != null)
        {
            return clip;
        }

        if (!searchAll)
        {
            return null;
        }

        foreach (var loaded in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (loaded.name == name)
            {
                return loaded;
            }
        }

        return null;
    }

    /// <summary>
    /// 歌曲另存一份成脚本，编辑器整首解码后播（浏览器在 OGG、VBR MP3 里跳播放位置不准，见 <see cref="AudioScript"/>）。
    /// 写不出来只是退回用 &lt;audio&gt; 播，不影响打开编辑器。
    /// </summary>
    private static void WriteAudioScript(string songFile, SessionAudio audio)
    {
        var length = new FileInfo(songFile).Length;
        if (!AudioScript.Wanted(Path.GetExtension(songFile), length))
        {
            return;
        }

        var script = AudioScript.PathFor(audio.File);
        try
        {
            using (var source = File.OpenRead(songFile))
            using (var target = new FileStream(Path.Combine(Folder, script), FileMode.Create, FileAccess.Write))
            {
                AudioScript.Write(source, target, audio.File);
            }

            audio.Script = script;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Main.Mod.Logger.Warning($"编辑器：另存歌曲数据失败（{ex.Message}），编辑器里跳转播放位置可能不准");
        }
    }

    /// <returns>读不出采样时 false（日志里写明），这时没有波形、官方关卡也没有音乐。</returns>
    private static bool TryDump(AudioClip clip, Stream? wav, SessionAudio audio)
    {
        if (GetData == null)
        {
            Main.Mod.Logger.Warning("编辑器：找不到 AudioClip.GetData(float[], int)，不导出音乐和波形");
            return false;
        }

        using var dump = new AudioDump(wav, clip.channels, clip.frequency, PeaksPerSecond);

        var buffer = new float[ChunkFrames * clip.channels];
        for (var frame = 0; frame < clip.samples; frame += ChunkFrames)
        {
            var frames = Math.Min(ChunkFrames, clip.samples - frame);
            var chunk = frames == ChunkFrames ? buffer : new float[frames * clip.channels];
            if (!GetData(clip, chunk, frame))
            {
                Main.Mod.Logger.Warning($"编辑器：读音频采样失败（第 {frame} 帧）");
                return false;
            }

            dump.Write(chunk, frames * clip.channels);
        }

        dump.Finish();
        audio.PeaksPerSecond = dump.PeaksPerSecond;
        audio.Peaks = dump.Peaks;
        return true;
    }

    /// <summary>
    /// 用系统外壳打开本地网页或文件夹（只开 Mod 自己目录里的东西）。和 Browser.Open 一样交给 explorer.exe，
    /// 浏览器不挂在游戏进程下面（否则 Steam 一直显示游戏在运行）。
    /// </summary>
    private static void OpenPage(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(Main.Mod.Path), StringComparison.OrdinalIgnoreCase))
        {
            Main.Mod.Logger.Warning($"不打开 Mod 目录以外的路径：{full}");
            return;
        }

        if (Application.platform != RuntimePlatform.WindowsPlayer)
        {
            Application.OpenURL(new Uri(full).AbsoluteUri);
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{full}\"") { UseShellExecute = false });
        }
        catch (Win32Exception ex)
        {
            Main.Mod.Logger.Warning($"用 explorer.exe 打开失败，改用 Application.OpenURL：{ex.Message}");
            Application.OpenURL(new Uri(full).AbsoluteUri);
        }
    }
}
