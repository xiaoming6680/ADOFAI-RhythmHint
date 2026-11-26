using System;
using System.Collections.Generic;
using System.IO;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 每帧决定画什么：在关卡里画关卡（换关时重新排键），在设置页里画演示，
/// 并处理入场 / 出场动画。绘制交给 TopBarView 和 LaneView。
/// </summary>
internal sealed class Overlay : MonoBehaviour
{
    /// <summary>最后一个音符过去多久后出场。</summary>
    private const double ExitAfterLastNote = 0.8;

    private readonly DisplayClock _clock = new();
    private readonly SmoothSongClock _smooth = new();
    private readonly PresenceAnimator _presence = new();
    private readonly KeyInput _keys = new();
    private GameViewport _viewport = null!;
    private readonly SettingsWindow _window = new();
    private string _hotkeyText = string.Empty;
    private KeyCode _hotkey = KeyCode.None;
    private bool _hotkeyBroken;
    private DemoPlayer _demo = null!;
    private InputShield _shield = null!;
    private (int, int, int, double, bool)? _fingerprint;
    private int _hitCount;
    private bool _rebuildRequested;
    private bool _demoShowing;
    private bool _uiFollowing;
    private readonly PlayStateTracker<States> _editorPlay = new();
    private string _builtFingering = string.Empty;
    private bool _failed;
    private bool _preloaded;
    private float _previewUntil;
    private Message _screenState = Message.Empty;
    private string _screenStateText = string.Empty; // 原文，用来判断画面状态变没变
    private Message _levelStatus = Lang.M("尚未进入关卡");
    private readonly FingeringSource _fingerings = new();
    private readonly JudgmentReader _judgments = new();
    private bool _playing; // 上一帧在打歌（关卡显示中、没暂停、没开设置窗口）：手序文件夹少看几次
    private LevelSnapshot? _snapshot;
    private bool _levelActive;
    private (int, int, int, double, bool)? _waitingPrint; // 还没开始的那一关：读过的指纹、排键设置和快照
    private string _waitingKey = string.Empty;
    private LevelSnapshot? _waitingSnapshot;
    private Message _caption = Message.Empty;
    private float _captionUntil;
    private string _captionShownFor = string.Empty;
    private bool _beatsFailed;      // 节拍线推算出过错（只记一次日志）
    private bool _difficultyFailed; // 难度估算出过错（同上）
    private bool _drawFailed;       // 画面板出过错（同上）
    private ScreenKey _screenKey;   // 画面状态的原因（见 ReportScreen），变了才拼成文字

    internal OverlayFrame Frame { get; } = new();

    /// <summary>给设置页显示的当前状态（按当前界面语言）。</summary>
    internal string Status => _failed ? Lang.T(_levelStatus) : Lang.T("{0}\n画面：{1}", _levelStatus, _screenState);

    /// <summary>「重新排键」：下一帧按现在的设置重排，并在按钮下面说一声。</summary>
    internal void RequestRebuild()
    {
        _rebuildRequested = true;
        Report(RulesArea, (Lang.M("已按当前设置重新排键。"), true));
    }

    /// <summary>设置窗口按钮的结果（2026-11-18 用户：设置页面缺少反馈），在按下的那排按钮下面显示几秒。</summary>
    internal ActionFeedback Feedback { get; } = new();

    /// <summary>排键页「专属手序」那排按钮。</summary>
    internal const string EditorArea = "editor";

    /// <summary>排键页底部的「重新排键」。</summary>
    internal const string RulesArea = "rules";

    private void Report(string area, (Message Text, bool Ok) result) => Feedback.Show(area, result.Text, !result.Ok, Time.unscaledTime);

    /// <summary>Mod 正显示的这一关（编辑本关手序用）；不在关卡里为 null。</summary>
    internal CurrentLevel<LevelSnapshot> Current { get; } = new();

    /// <summary>这一关用的是哪份手序（设置窗口显示）。</summary>
    internal string FingeringStatus => Current.Snapshot != null
        ? Lang.T(Current.Snapshot.Fingering.Status)
        : Lang.T("不在关卡中。进入关卡或在编辑器中试玩时，会自动使用对应歌曲的专属手序。");

    /// <summary>「编辑本关手序」的说明（按下后的结果见 <see cref="Feedback"/>）。</summary>
    internal string EditorMessage => Current.Snapshot == null ? Lang.T("进入关卡或在编辑器中试玩时，可以编辑这一关的手序（可以先暂停）。")
        : Lang.T("在浏览器中打开手序编辑器，自动带上这一关的砖块、音乐和两种自动排法做参考；保存后马上生效。");

    /// <summary>「编辑本关手序」：把这一关交给编辑器并打开。</summary>
    internal void OpenEditor()
    {
        var snapshot = Current.Snapshot;
        if (snapshot == null || !GameReader.TryGetLevel(out var level, out _))
        {
            Report(EditorArea, (Lang.M("请先进入关卡或在编辑器中试玩。"), false));
            return;
        }

        try
        {
            Report(EditorArea, EditorLauncher.Open(snapshot, level, _fingerings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            // 写不了编辑器目录（权限、磁盘满），这一关的数据凑不成一份手序，或者排键参数不合法（编辑器两种排法都排一遍，
            // 读设置时已修正过，这里兜底）、路径里有非法字符：告诉玩家，提示面板照常工作
            Report(EditorArea, (Lang.M("打开手序编辑器失败：{0}", ex.Message), false));
            Main.Mod.Logger.LogException(ex);
        }
    }

    /// <summary>玩家给这一关选手序（文件名；空串 = 都不用；null = 忘掉选择，重新自动识别）。</summary>
    internal void ChooseFingering(string? name)
    {
        var snapshot = Current.Snapshot;
        if (snapshot == null || snapshot.Fingering.Chart.Length == 0)
        {
            return;
        }

        FingeringSource.Remember(Main.Settings, snapshot.Fingering.Chart, name);
        Main.Settings.Save(Main.Mod);
        _rebuildRequested = true;
        _waitingPrint = null;
    }

    /// <summary>「创建桌面快捷方式」：不开游戏也能打开编辑器。</summary>
    internal void CreateEditorShortcut()
    {
        try
        {
            Report(EditorArea, EditorLauncher.CreateShortcut());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Report(EditorArea, (Lang.M("无法创建桌面快捷方式：{0}", ex.Message), false));
            Main.Mod.Logger.LogException(ex);
        }
    }

    /// <summary>「打开手序文件夹」。</summary>
    internal void OpenFingeringFolder()
    {
        try
        {
            EditorLauncher.OpenFolder();
            Report(EditorArea, (Lang.M("已打开手序文件夹。"), true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(EditorArea, (Lang.M("无法打开手序文件夹：{0}", ex.Message), false));
            Main.Mod.Logger.LogException(ex);
        }
    }

    /// <summary>设置页每帧调用：设置页开着时播放演示。</summary>
    internal void RequestPreview() => _previewUntil = Time.unscaledTime + 0.6f;

    /// <summary>打开独立设置窗口（UMM 页面的按钮调用）。</summary>
    internal void OpenSettings()
    {
        if (!_window.Visible)
        {
            _window.Toggle();
        }
    }

    private void Awake()
    {
        _demo = new DemoPlayer(gameObject);
        _shield = new InputShield(transform);
        _viewport = new GameViewport(transform);
    }

    private void OnDestroy()
    {
        // Mod 被关掉时把游戏画面还原；自动更新的协程跟着停了，把卡在中间的状态复位
        _viewport.Restore();
        AutoUpdater.Abort();
    }

    private void Update()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            Lang.Update(Main.Settings);
            if (!_preloaded)
            {
                _preloaded = true;
                AutoUpdater.Start(this);
                Preload();
            }

            UpdateNotice.Update(Main.Settings);
            ReadHotkey();
            if (_window.Visible)
            {
                RequestPreview(); // 设置窗口开着时播放演示
            }

            Tick();

            // 设置窗口底下垫透明挡板：在窗口里点击、拖动不会被游戏当成击打或点到后面的按钮
            _shield.Cover(_window.ScreenRect);
        }
        catch (Exception ex)
        {
            // 游戏更新后接口变了之类的意外：记一次完整日志后停用叠加层，避免每帧刷屏、拖慢游戏
            _failed = true;
            _demo.Stop();
            _viewport.Restore();
            _shield.Cover(null);
            Frame.Presence = 0;
            _levelStatus = Lang.M("发生错误，已停用：{0}（详见 Unity Mod Manager 日志）", ex.Message);
            Main.Mod.Logger.LogException(ex);
        }
    }

    /// <summary>
    /// 游戏启动后第一帧（还在载入）先把设置窗口的图标和字形准备好，打开窗口、换页时不卡（见 WindowSkin.Preload）。
    /// 只是提速：出错就记日志，窗口照常能用（第一次画到时再读），所以这里接住所有异常，不让它停用整个叠加层。
    /// </summary>
    private static void Preload()
    {
        try
        {
            WindowSkin.Preload();
        }
        catch (Exception ex)
        {
            Main.Mod.Logger.Warning($"设置窗口预先准备失败，打开时再读：{ex.Message}");
            Main.Mod.Logger.LogException(ex);
        }

        WarmPlanner();
    }

    /// <summary>
    /// 6K、8K 的排键代码第一次跑时要先编译（2026-11-22 用户：第一次用 6K、8K 时进场会卡一下）：游戏启动时拿一小段谱
    /// （慢、中速、快连打、多押）各排一遍。出错只记日志，进关卡时照常排。
    /// </summary>
    private static void WarmPlanner()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var tiles = new List<TileHit>();
            var t = 1.0;
            foreach (var gap in new[] { 0.3, 0.3, 0.1, 0.1, 0.04, 0.04, 0.04, 0.04, 0.04, 0.04, 0.04, 0.3, 0.02, 0.02, 0.3 })
            {
                t += gap;
                tiles.Add(new TileHit(tiles.Count + 1, t, Angle: gap < 0.03 ? 15 : 180));
            }

            foreach (var keys in KeyLayout.Modes)
            {
                NotePlanner.Plan(tiles, new FingeringOptions { Keys = keys });
            }

            Main.Mod.Logger.Log($"排键：先跑一遍 4、6、8 键（{watch.ElapsedMilliseconds} 毫秒）");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Main.Mod.Logger.Warning($"排键预热失败，进关卡时照常排：{ex.Message}");
        }
    }

    /// <summary>设置窗口的快捷键（默认 F8）。按键名在设置里改，写错了就当没有快捷键。</summary>
    private void ReadHotkey()
    {
        if (_hotkeyBroken)
        {
            return;
        }

        if (Main.Settings.SettingsHotkey != _hotkeyText)
        {
            _hotkeyText = Main.Settings.SettingsHotkey;
            _hotkey = KeyCodes.Parse(_hotkeyText.Trim());
        }

        try
        {
            if (_hotkey != KeyCode.None && Input.GetKeyDown(_hotkey))
            {
                _window.Toggle();
            }
        }
        catch (InvalidOperationException ex)
        {
            // 游戏关掉了旧版输入系统时会抛这个：只能从 UMM 页面的按钮打开设置窗口
            _hotkeyBroken = true;
            Main.Mod.Logger.Warning($"无法读取键盘输入，设置窗口只能从 Unity Mod Manager 页面打开：{ex.Message}");
        }
    }

    private void Tick()
    {
        var settings = Main.Settings;

        // 手序文件夹有变化（比如编辑器刚保存）：关卡里马上重排，换上新的手序
        if (_fingerings.Poll(_playing) && settings.UseCustomFingering)
        {
            _rebuildRequested = true;
            _waitingPrint = null;
        }

        var inLevel = GameReader.TryGetLevel(out var level, out var reason);
        var levelShown = inLevel && TickLevel(level, settings, out reason);
        if (!inLevel)
        {
            _fingerprint = null;
            _editorPlay.Reset();
            _waitingPrint = null;
            _waitingSnapshot = null;
        }

        // 能编辑这一关手序的时候：Mod 正显示这一关（游玩、暂停、死亡、通关画面），或者关卡已载入、还没按键开始；离开关卡就清掉
        if (inLevel && _levelActive && _snapshot != null && _fingerprint != null)
        {
            Current.Show(_snapshot);
        }
        else if (inLevel && _window.Visible && WaitingSnapshot(level) is { } waiting)
        {
            Current.Show(waiting);
        }
        else
        {
            Current.Leave();
        }

        var preview = !levelShown && settings.ShowPreview && Time.unscaledTime < _previewUntil;

        // 设置页关掉后演示还在滑出：音符照常走（不再放嗒声），完全滑出后才停
        var demoRunning = !levelShown && (preview || (_demoShowing && Frame.Presence > 0));
        if (demoRunning)
        {
            _demo.Tick(Frame, settings, sound: preview, sampleJudgments: _window.OnLookPage);
            _demoShowing = true;
            _fingerprint = null; // 演示用过 Frame，回到关卡时要重新装入关卡的音符
        }
        else
        {
            _demo.Stop();
            _demoShowing = false;
        }

        // 按 Esc 暂停时面板收起、游戏画面还原，不挡暂停菜单；暂停中打开本 Mod 的设置窗口时照常显示，方便看改设置的效果
        var paused = levelShown && level.Controller.paused;
        var pausedHide = paused && !_window.Visible;
        _playing = levelShown && !paused && !_window.Visible;
        Frame.Presence = (float)_presence.Update((levelShown && !pausedHide) || preview, Time.unscaledDeltaTime);
        Frame.Judgments.Expire(Time.unscaledTime);

        // 界面文字在关卡里始终跟着面板一起缩放（暂停、开关本 Mod 设置窗口、本关结束都一样）：
        // 面板滑出时一起放大回全屏，完全收起时正好和原样重合，这时才改回覆盖模式；中途不切换，否则会先跳一下再动。
        // 离开关卡后面板还在滑出时也继续跟着，滑完为止
        _uiFollowing = levelShown || (_uiFollowing && Frame.Presence > 0);
        PanelMotion.Update(settings, Frame.Keys, Frame.Presence > 0);
        _viewport.Apply(Frame.Presence, settings, _uiFollowing, Frame.Keys);
        ReportScreen(pausedHide, levelShown, level, preview, reason);
        _keys.Read(settings, Frame);
    }

    /// <returns>这一帧关卡是否应该显示；不显示时 reason 写明原因。</returns>
    private bool TickLevel(LevelState level, Settings settings, out Message reason)
    {
        // 编辑器不会清掉上一次留下的「通关 / 失败」：这次播放中状态变过以后才算通关或死亡（见 PlayStateTracker）
        var fresh = _editorPlay.Update(level.EditorPlaying, level.State);
        var stale = ADOBase.isLevelEditor && !fresh;
        var won = level.Won && !stale;
        var failed = level.Failed && !stale;
        _levelActive = false;

        if (!level.ShouldDraw)
        {
            reason = Lang.M("游戏状态：{0}", level.StateName);
            return false;
        }

        if (level.Floors[level.Floors.Count - 1].entryTime <= 0)
        {
            reason = Lang.M("砖块时间尚未计算完成");
            return false;
        }

        var fp = GameReader.Fingerprint(level);
        if (_rebuildRequested || _fingerprint != fp || settings.FingeringKey != _builtFingering)
        {
            _rebuildRequested = false;
            _fingerprint = fp;
            Rebuild(level);
        }

        if (_hitCount == 0)
        {
            // 一块要按的砖都没有：不显示（否则一出来就判成「本关结束」），各类砖的数量已在 Rebuild 里写进日志
            reason = Lang.M("本关没有需要按键的砖块");
            return false;
        }

        _levelActive = true;
        EditorLauncher.RememberHitSound(level); // 存一份这一关的打击声，还没开始时打开编辑器也能用原声
        Frame.Caption = Time.unscaledTime < _captionUntil ? _caption : Message.Empty;

        // 游戏的歌曲进度每次对齐音频时钟都会跳一下，摊平成每帧均匀地走（见 SmoothSongClock）；
        // 失败画面期间定住，不让音符继续流动；重开或回检查点后恢复
        var conductor = level.Conductor;
        var song = _smooth.Advance(conductor.songposition_minusi, Time.unscaledTimeAsDouble,
            conductor.song != null ? conductor.song.pitch : 1f, !level.Controller.paused);
        Frame.Now = _clock.Advance(song + (settings.VisualOffsetMs / 1000.0), failed);
        Frame.Bpm = GameReader.CurrentBpm(level);
        Frame.TrackHits(level.Controller.currentSeqID);
        Frame.RealBpm = (float)RealBpm.At(Frame.Notes, Frame.NextNote, conductor.song != null ? conductor.song.pitch : 1f);
        _judgments.Update(level, Frame, settings);

        if (won || (!failed && Frame.NextNote >= Frame.Notes.Count && Frame.Now > Frame.EndTime + ExitAfterLastNote))
        {
            reason = Lang.M("本关结束");
            return false; // 出场动画
        }

        reason = Message.Empty;
        return true;
    }

    /// <summary>只按这一关自己的砖块排键，不读任何外部谱面。</summary>
    /// <summary>读这一关：砖块、节拍线、BPM，以及要用的音符（文件夹里有这首歌的专属手序就用，对不上的地方用自动排键）。</summary>
    private LevelSnapshot ReadLevel(LevelState level)
    {
        var hits = GameReader.ReadHits(level);
        var end = hits.Count > 0 ? hits[hits.Count - 1].Time : 0;

        // 节拍线从起点砖（第 0 块）开始算，双手稳拍也用这份拍线。
        // 拍子和小节从关卡变速和砖块推（BeatGrid.Infer）；交给编辑器的 tempo 仍是关卡的速度（编辑器按它算轨道预览的星球转向）
        var speeds = GameReader.ReadSpeeds(level);
        var crotchet = GameReader.Crotchet(level);
        var tempo = BeatGrid.MusicalTempo(speeds, crotchet);
        var settings = Main.Settings;
        var options = settings.ToFingering();
        var start = level.Floors[0].entryTime;
        var steady = options.Style == FingeringStyle.SteadyHands;
        var beats = InferBeats(hits, speeds, crotchet, tempo, start, end + 2, steady);
        // 双手稳拍（试验性）参考拍线和进砖方向；别的排法只看砖块间隔
        var rhythm = steady
            ? new FingeringRhythm(start, tempo, FingeringRhythm.IncomingDirections(GameReader.ReadPath(level, hits)), beats)
            : null;

        // 先估难度（只看这一关的砖块，按关卡设定的音高换算成实际播放的时间，不算练习时临时调的速度：TUF 按原速评级），
        // 再定几键（自动选键数按它），最后按这个键数排键、套专属手序（用上的手序是 4、6、8 键时自动模式听它的）
        var data = GameReader.LevelData();
        var pitch = data != null && data.pitch > 0 ? data.pitch / 100.0 : 1;
        var tier = EstimateTier(hits, pitch);
        var keyChoice = settings.KeyCountFor(tier);
        var plans = new Dictionary<int, List<PlannedNote>>();
        List<PlannedNote> AutoFor(int keys)
        {
            if (!plans.TryGetValue(keys, out var notes))
            {
                notes = NotePlanner.Plan(hits, settings.ToFingering(keys), rhythm);
                plans[keys] = notes;
            }

            return notes;
        }

        var info = GameReader.ReadInfo(level);
        var chosen = hits.Count > 0
            ? _fingerings.Choose(hits, AutoFor, settings, info, keyChoice)
            : new FingeringResult(AutoFor(keyChoice.Keys), new List<bool>(), string.Empty, null, Message.Empty, Message.Empty)
            {
                KeyChoice = keyChoice,
                Keys = keyChoice.Keys,
            };
        var bpm = tempo.ConvertAll(t => (t.Time, 60 / t.SecondsPerBeat));
        return new LevelSnapshot(hits, chosen, info, beats, bpm, tier);
    }

    /// <summary>
    /// 节拍线和小节（BeatGrid.Infer）。只是画出来看的：推算出错（关卡数据异常）时退回按关卡变速直接画（BeatGrid.Build），
    /// 不让它停用整个 Mod；只在第一次出错时记日志，免得每次重排都刷一遍。
    /// </summary>
    private List<BeatLine> InferBeats(List<TileHit> hits, List<(double Time, double Speed)> speeds, double crotchet,
        List<(double Time, double SecondsPerBeat)> tempo, double start, double end, bool steady)
    {
        try
        {
            var grid = steady ? BeatGrid.InferForSteadyHands(hits, speeds, crotchet, start, end)
                : BeatGrid.Infer(hits, speeds, crotchet, start, end);
            Main.Mod.Logger.Log($"节拍线：{grid.Summary()}");
            return grid.Lines;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArithmeticException or IndexOutOfRangeException)
        {
            // 纯计算遇到异常数据会抛的这几种（空序列、参数越界、溢出）：节拍线改用关卡变速直接画
            if (!_beatsFailed)
            {
                _beatsFailed = true;
                Main.Mod.Logger.Warning($"节拍线推算出错，改按关卡变速画（之后再出错不再记录）：{ex.Message}");
                Main.Mod.Logger.LogException(ex);
            }

            return BeatGrid.Build(tempo, start, end);
        }
    }

    /// <summary>估算难度（试验性）：出错时不显示难度徽章，只在第一次出错时记日志。</summary>
    private int EstimateTier(List<TileHit> hits, double pitch)
    {
        try
        {
            var difficulty = DifficultyEstimator.Estimate(hits, pitch);
            Main.Mod.Logger.Log($"难度估算：{DifficultyTiers.Name(difficulty.Tier)}（{difficulty.Score:F1}，音高 {pitch:0.##}）");
            return hits.Count > 1 ? difficulty.Tier : 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArithmeticException or IndexOutOfRangeException)
        {
            // 同 InferBeats：只是一个参考徽章，算不出来就不画
            if (!_difficultyFailed)
            {
                _difficultyFailed = true;
                Main.Mod.Logger.Warning($"难度估算出错，这一关不显示难度（之后再出错不再记录）：{ex.Message}");
                Main.Mod.Logger.LogException(ex);
            }

            return 0;
        }
    }

    /// <summary>
    /// 关卡已载入、还没按键开始（Start）时这一关的快照：这时 Mod 不显示面板，只在设置窗口开着时现读一遍（同一关、同样的设置只读一次）。
    /// 不是这种情况、或者没有要按的砖时为 null。
    /// 复现（2026-11-18 用户）：进关卡先按 F8，点「编辑本关手序」没反应——原来只有 Mod 正显示这一关时才能编辑。
    /// </summary>
    private LevelSnapshot? WaitingSnapshot(LevelState level)
    {
        var floors = level.Floors;
        if (ADOBase.isLevelEditor || level.State != States.Start || floors.Count < 2 || floors[floors.Count - 1].entryTime <= 0)
        {
            return null;
        }

        var print = GameReader.Fingerprint(level);
        var key = Main.Settings.FingeringKey;
        if (_waitingPrint != print || _waitingKey != key)
        {
            _waitingPrint = print;
            _waitingKey = key;
            var snapshot = ReadLevel(level);
            _waitingSnapshot = snapshot.Hits.Count > 0 ? snapshot : null;
        }

        return _waitingSnapshot;
    }

    private void Rebuild(LevelState level)
    {
        var snapshot = ReadLevel(level);
        var hits = snapshot.Hits;
        _hitCount = hits.Count;
        _builtFingering = Main.Settings.FingeringKey;
        var end = hits.Count > 0 ? hits[hits.Count - 1].Time : 0;
        var info = snapshot.Info;
        var chosen = snapshot.Fingering;
        var notes = chosen.Notes;
        Frame.Load(notes, hits, snapshot.Beats, end, chosen.Keys);
        _judgments.Reset();
        Frame.Caption = Message.Empty;
        _snapshot = hits.Count > 0 ? snapshot : null;
        Frame.DifficultyTier = snapshot.Tier;

        // 对不上、差别太大：开局在面板上提醒一下（同一首歌、同一个结果只提醒一次，重开不再提醒）
        var captionKey = $"{info.Artist}|{info.Song}|{chosen.UsedFile}|{chosen.Caption.Source}";
        if (chosen.Caption.Source.Length > 0 && captionKey != _captionShownFor)
        {
            _caption = chosen.Caption;
            _captionUntil = Time.unscaledTime + 8;
            _captionShownFor = captionKey;
        }

        var keys = chosen.Keys;
        var chords = new int[keys + 1]; // 按几个键：双押、三押、四押及以上各几个
        var laneUse = new int[keys];
        foreach (var n in notes)
        {
            chords[n.Lanes.Count]++;
            foreach (var lane in n.Lanes.Keys)
            {
                laneUse[lane]++;
            }
        }

        var many = 0;
        for (var k = 4; k <= keys; k++)
        {
            many += chords[k];
        }

        var byAngle = hits.Exists(h => !double.IsNaN(h.Angle));
        _levelStatus = Lang.M("本关使用 {0} 键（{1}），共 {2} 次按键，生成 {3} 个提示（双押 {4}、三押 {5}、四押及以上 {6} 个，{7}），各轨道音符数 [{8}]{9}",
            keys, KeyReason(chosen.KeyChoice, snapshot.Tier, chosen.UsedFile.Length > 0), hits.Count, notes.Count, chords[2], chords[3], many,
            byAngle ? Lang.M("按转角判断") : Lang.M("按时间判断"),
            string.Join(", ", laneUse), hits.Count == 0 ? Lang.M("；{0}", GameReader.DescribeFloors(level)) : Message.Empty);
        Main.Mod.Logger.Log(_levelStatus.ToString());
    }

    /// <summary>这一关为什么按这个键数显示（状态和日志里写）。</summary>
    private static Message KeyReason(KeyCountChoice choice, int tier, bool usedFingering) => choice.Reason switch
    {
        KeyCountReason.Manual => Lang.M("手动设置"),
        _ when usedFingering => Lang.M("依据专属手序"),
        KeyCountReason.NoEstimate => Lang.M("无法估算难度"),
        _ => Lang.M("依据估算难度 {0}", DifficultyTiers.Name(tier)),
    };

    /// <summary>
    /// 这一帧画面状态的原因（只有值）：每帧比一次，变了才拼成消息和文字（原来每帧拼一次约 270 字节）。
    /// Kind：1 暂停收起、2 显示关卡、3 显示演示、4 隐藏（隐藏的原因另外按内容比，见 <see cref="SameMessage"/>）。
    /// </summary>
    private readonly record struct ScreenKey(int Kind, States State, bool Editor, bool EditorPlaying, bool Music, double SecondsPerBeat);

    private Message _hiddenReason = Message.Empty;

    private void ReportScreen(bool pausedHide, bool levelShown, LevelState level, bool preview, Message reason)
    {
        var key = pausedHide ? new ScreenKey(1, default, false, false, false, 0)
            : levelShown ? new ScreenKey(2, level.State, ADOBase.isLevelEditor, level.EditorPlaying, false, 0)
            : preview ? new ScreenKey(3, default, false, false, _demo.FollowingMusic, _demo.SecondsPerBeat)
            : new ScreenKey(4, default, false, false, false, 0);
        if (key == _screenKey && (key.Kind != 4 || SameMessage(reason, _hiddenReason)))
        {
            return;
        }

        _screenKey = key;
        _hiddenReason = reason;
        Report(key.Kind switch
        {
            1 => Lang.M("已暂停，面板收起"),
            2 => Lang.M("显示关卡（{0}）", level.StateName),
            3 => Lang.M("显示演示（{0}）", _demo.Mode),
            _ => Lang.M("隐藏（{0}）", reason),
        });
    }

    /// <summary>两句话的原文和参数都一样（参数里的消息也逐层比），不用拼成文字就能比。</summary>
    private static bool SameMessage(Message a, Message b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Source != b.Source || a.Args.Length != b.Args.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Args.Length; i++)
        {
            var same = a.Args[i] is Message x && b.Args[i] is Message y ? SameMessage(x, y) : Equals(a.Args[i], b.Args[i]);
            if (!same)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>画面状态（显示什么 / 为什么隐藏）变化时记一行日志，方便查「为什么没显示」。</summary>
    private void Report(Message state)
    {
        var text = state.ToString();
        if (text == _screenStateText)
        {
            return;
        }

        _screenState = state;
        _screenStateText = text;
        Main.Mod.Logger.Log($"画面：{text}");
    }

    private void OnGUI()
    {
        GUI.depth = 100; // 让 UMM 设置窗口等其它界面盖在上面
        if (Frame.Presence > 0 && Event.current.type == EventType.Repaint)
        {
            try
            {
                DrawOverlay();
            }
            catch (Exception ex) when (ex is not ExitGUIException)
            {
                // 画到一半出错（不该发生）：这一帧不画完，下一帧照常再画；完整日志只记一次，免得每帧刷错误拖慢游戏。
                // 画面板用的裁剪组（GUI.BeginGroup）要在 try/finally 里关上，否则后面的设置窗口也会被裁进去
                if (!_drawFailed)
                {
                    _drawFailed = true;
                    Main.Mod.Logger.Error($"画提示面板出错（之后再出错不再记录）：{ex.Message}");
                    Main.Mod.Logger.LogException(ex);
                }
            }
        }

        // 设置窗口要处理鼠标、键盘等所有事件，不能只在 Repaint 时画；关掉后还要画完淡出动画
        if (_window.NeedsDraw && !_failed)
        {
            _window.Draw(this);
        }
        else if (!_failed)
        {
            _window.Prewarm(this); // 还没打开过：趁游戏载入时在后台把每一页排一遍，打开、换页时不卡
        }

        if (!_failed && Event.current.type == EventType.Repaint && Frame.Presence <= 0 && Time.frameCount % 30 == 0)
        {
            LaneView.Prewarm(Main.Settings); // 面板没显示时把键名字形先排好，进场时不卡；隔一会儿看一次，没变化时直接返回
        }
    }

    private void DrawOverlay()
    {
        // 游戏画面缩进去之后，让出来的区域没有摄像机在画，会残留旧画面：先铺纯黑，再画面板
        var freed = _viewport.Freed;
        Black(new Rect(0, 0, (float)freed.Left, Screen.height));
        Black(new Rect(Screen.width - (float)freed.Right, 0, (float)freed.Right, Screen.height));
        Black(new Rect(0, 0, Screen.width, (float)freed.Top));
        Black(new Rect(0, Screen.height - (float)freed.Bottom, Screen.width, (float)freed.Bottom));

        var settings = Main.Settings;
        var layout = new OverlayLayout(settings, Frame.Keys, PanelMotion.Pose);
        var p = Frame.Presence;
        // 面板从它那一边滑入 / 滑出：左边往左、右边往右，在中间时往下（换位置的途中按份量混合）。
        // 只剩 BPM 框（太鼓条关着）时它跟着面板那一列滑；设置里关掉下落面板时面板自己滑出去
        var pose = layout.Pose;
        Vector3 Away(float shown) => new((1 - shown) * (layout.PanelWidth + 8) * (pose.Right - pose.Left), (1 - shown) * layout.PanelHeight * 0.35f * pose.Center, 0);
        var lanesShown = p * PanelMotion.LanesShown;
        Painter.Alpha = p;
        var saved = GUI.matrix;
        try
        {
            var row = Mathf.Max(pose.Taiko, pose.Bpm);
            if (row > 0.001f)
            {
                // 进出关卡时太鼓条从它那一边（上或下）滑入 / 滑出；只剩 BPM 框（太鼓条关着）时框跟着下落面板那一列滑。
                // 设置里开关太鼓条、BPM 框时（pose.Taiko、pose.Bpm 在缓动）位置由 OverlayLayout 算，这里再按显示了多少淡入淡出
                var away = new Vector3(0, (1 - p) * (layout.RowHeight + 8) * ((2 * pose.Bottom) - 1), 0);
                GUI.matrix = saved * Matrix4x4.Translate(away);
                if (pose.Taiko > 0.001f)
                {
                    Painter.Alpha = p * pose.Taiko;
                    TopBarView.DrawStrip(Frame, layout, settings);
                }

                if (pose.Bpm > 0.001f)
                {
                    Painter.Alpha = p * pose.Bpm;
                    GUI.matrix = saved * Matrix4x4.Translate(Vector3.Lerp(Away(p), away, pose.Taiko));
                    TopBarView.DrawBox(Frame, layout, settings);
                    GUI.matrix = saved * Matrix4x4.Translate(away);
                }

                if (pose.Taiko > 0.001f)
                {
                    Painter.Alpha = p * pose.Taiko;
                    TopBarView.DrawLine(layout);
                }

                Painter.Alpha = p * row;
                TopBarView.DrawCaption(Frame, layout, settings);
            }

            if (lanesShown > 0.001f)
            {
                Painter.Alpha = lanesShown;
                GUI.matrix = saved * Matrix4x4.Translate(Away(lanesShown));
                LaneView.Draw(Frame, layout, settings);
            }
        }
        finally
        {
            GUI.matrix = saved;
            Painter.Alpha = 1f;
        }
    }

    private static void Black(Rect r)
    {
        if (r.width > 0 && r.height > 0)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.black, 0f, 0f);
        }
    }
}
