using System;
using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 设置页的演示：循环播放一段内置节奏，所有设置改了立刻能看到效果。
/// <list type="bullet">
/// <item>主菜单 / 选关界面有音乐时，演示跟着菜单音乐的拍子走（用游戏自己的歌曲进度和 BPM），
/// 音符落到判定线时应正好踩在拍子上，可以直接检查校准</item>
/// <item>没有音乐时用内置 160 BPM，每个音符按音频时钟播放一声「嗒」做参照</item>
/// </list>
/// </summary>
internal sealed class DemoPlayer
{
    private const int Loops = 3;
    private const double ScheduleAhead = 0.2;
    private const double FallbackBpm = 160;

    private readonly AudioSource[] _sources;
    private readonly SmoothSongClock _smooth = new();
    private DemoChart _demo = DemoChart.Build(FallbackBpm);
    private double[] _tileTimes = Array.Empty<double>();
    private int _nextSource;
    private double _builtBpm = double.NaN; // 演示谱按哪个 BPM（取到 0.001）和哪组排键设置排的，两样都没变就不重排
    private string _builtKey = string.Empty;
    private int _builtKeys;
    private bool _running;
    private bool _followingMusic;
    private double _startDsp;
    private int _clickIndex;
    private int _clickLoop;

    public DemoPlayer(GameObject host)
    {
        var click = MakeClick();
        _sources = new AudioSource[6];
        for (var i = 0; i < _sources.Length; i++)
        {
            var s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.ignoreListenerPause = true; // 游戏暂停时演示也要有声音
            s.clip = click;
            _sources[i] = s;
        }
    }

    /// <summary>当前是否跟着主菜单音乐（日志和状态显示用）。</summary>
    public Message Mode => _followingMusic
        ? Lang.M("跟随菜单音乐，{0:0.#} BPM", 60 / _demo.SecondsPerBeat)
        : Lang.M("内置节拍，{0:0} BPM", FallbackBpm);

    /// <summary><see cref="Mode"/> 取决于这两样：每帧比它们，变了才拼 Mode（见 Overlay.ReportScreen）。</summary>
    public bool FollowingMusic => _followingMusic;

    public double SecondsPerBeat => _demo.SecondsPerBeat;

    /// <param name="sound">是否放嗒声（演示正在滑出时不放）。</param>
    /// <param name="sampleJudgments">出示例判定（设置窗口在外观页时）。</param>
    public void Tick(OverlayFrame frame, Settings settings, bool sound, bool sampleJudgments)
    {
        var music = TryMenuMusic(out var songPos, out var bpm, out var pitch);
        if (!_running || music != _followingMusic)
        {
            _running = true;
            _followingMusic = music;
            _startDsp = AudioSettings.dspTime + 0.1;
            _clickIndex = 0;
            _clickLoop = 0;
            _builtKey = string.Empty;
        }

        Rebuild(frame, settings, music ? bpm : FallbackBpm);

        // 菜单音乐的进度和音频时钟都是隔一段才动一下，画面用摊平后的时间（见 SmoothSongClock）
        double songNow;
        if (music)
        {
            songNow = _smooth.Advance(songPos, Time.unscaledTimeAsDouble, pitch, running: true);
        }
        else
        {
            // 用音频时钟驱动画面，保证画面和嗒声在同一条时间轴上；嗒声按原始的音频时钟排，画面用摊平的
            var audioNow = AudioSettings.dspTime - _startDsp;
            if (sound && settings.PreviewSound)
            {
                ScheduleClicks(audioNow, settings.PreviewVolume);
            }
            else
            {
                SkipClicks(audioNow);
            }

            songNow = _smooth.Advance(audioNow, Time.unscaledTimeAsDouble, 1, running: true);
        }

        // 永远显示三份演示里的中间一份，前后都有音符，循环无缝；第 0 拍对齐歌曲的第 0 拍
        var loop = _demo.LoopSeconds;
        var shown = loop + (((songNow % loop) + loop) % loop);
        frame.Now = shown + (settings.VisualOffsetMs / 1000.0);
        frame.EndTime = shown + loop;
        frame.TrackHits(CountReached(frame.Now));
        frame.RealBpm = (float)RealBpm.At(frame.Notes, frame.NextNote, music ? pitch : 1);
        if (sampleJudgments)
        {
            AddSampleJudgments(frame, settings);
        }
    }

    /// <summary>演示里每块砖都按得准，判定是编出来的（<see cref="JudgmentRules.DemoAt"/>），只为让玩家看到判定提示的样子。</summary>
    private static void AddSampleJudgments(OverlayFrame frame, Settings settings)
    {
        foreach (var note in frame.Passed)
        {
            var judgment = JudgmentRules.DemoAt(note.TileIndex);
            if (GameJudgments.Shows(judgment, settings))
            {
                frame.Judgments.Add(note.Lanes.Keys, judgment, Time.unscaledTime, GameJudgments.MinGap(frame.Keys));
            }
        }
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        foreach (var s in _sources)
        {
            s.Stop();
        }
    }

    /// <summary>主菜单 / 选关界面正在放音乐时，读游戏的歌曲进度和 BPM。</summary>
    private static bool TryMenuMusic(out double songPosition, out double bpm, out double pitch)
    {
        songPosition = 0;
        bpm = 0;
        pitch = 1;
        var conductor = ADOBase.conductor;
        // BPM 不是正的有限数（不该发生）时用内置节拍
        if (conductor == null || conductor.song == null || !conductor.song.isPlaying || !(conductor.bpm > 0) || float.IsInfinity(conductor.bpm))
        {
            return false;
        }

        songPosition = conductor.songposition_minusi;
        bpm = conductor.bpm;
        pitch = conductor.song.pitch;
        return true;
    }

    /// <summary>
    /// 自动选键数时演示按几键播（设置窗口「键数」那一组里点的预览，默认 4K）；手动选的时候按选的键数。不存进设置文件。
    /// </summary>
    public static int PreviewKeys { get; set; } = 4;

    /// <summary>演示按几键播。</summary>
    public static int DemoKeys(Settings settings) => settings.AutoKeyCount ? PreviewKeys : settings.KeyCountFor(0).Keys;

    /// <summary>BPM、排键参数或键数变了就重排演示谱，改完立刻能看到新键位。</summary>
    private void Rebuild(OverlayFrame frame, Settings settings, double bpm)
    {
        // 每帧调用：先比 BPM 和排键设置的指纹（设置没变时是同一个字符串），都没变就不重排，也不拼字符串
        var rounded = Math.Round(bpm, 3, MidpointRounding.AwayFromZero);
        var key = settings.FingeringKey;
        var keys = DemoKeys(settings);
        if (rounded.Equals(_builtBpm) && key == _builtKey && keys == _builtKeys)
        {
            return;
        }

        _builtBpm = rounded;
        _builtKey = key;
        _builtKeys = keys;
        _demo = DemoChart.BuildForSettings(bpm, settings.ActiveStyle, keys);
        var tiles = DemoChart.Repeat(_demo, Loops);
        _tileTimes = tiles.ConvertAll(t => t.Time).ToArray();
        var tempo = new[] { (0.0, _demo.SecondsPerBeat) };
        var beats = BeatGrid.Build(tempo, 0, _demo.LoopSeconds * Loops);
        var notes = NotePlanner.Plan(tiles, settings.ToFingering(keys), new FingeringRhythm(0, tempo, Beats: beats));
        frame.Load(notes, tiles, beats, _demo.LoopSeconds * Loops, keys);
        frame.DifficultyTier = DifficultyEstimator.Estimate(tiles).Tier; // 设置窗口里预览徽章的样子
        frame.Bpm = (float)bpm;
        frame.Caption = _followingMusic
            ? Lang.M("演示 · 跟随当前音乐 {0:0.#} BPM · 每轮前 4 拍逐拍出现音符，音符到达判定线时应与节拍一致，如有偏差请调整「画面偏移」", bpm)
            : Lang.M("演示 · {0:0} BPM · 请对照提示音调整「画面偏移」", bpm);
        _clickIndex = 0;
        _clickLoop = 0;
    }

    /// <summary>已经到达判定线的砖块数（演示里每块砖都「准时被按到」）。</summary>
    private int CountReached(double now)
    {
        var i = Array.BinarySearch(_tileTimes, now);
        return i >= 0 ? i + 1 : ~i;
    }

    private void ScheduleClicks(double elapsed, float volume)
    {
        while (true)
        {
            var at = NextClickTime();
            if (at > elapsed + ScheduleAhead)
            {
                return;
            }

            if (at >= elapsed)
            {
                var source = _sources[_nextSource];
                _nextSource = (_nextSource + 1) % _sources.Length;
                source.volume = Mathf.Clamp01(volume);
                source.PlayScheduled(_startDsp + at);
            }

            Advance();
        }
    }

    private void SkipClicks(double elapsed)
    {
        while (NextClickTime() < elapsed + ScheduleAhead)
        {
            Advance();
        }
    }

    private double NextClickTime() => _demo.Tiles[_clickIndex].Time + (_clickLoop * _demo.LoopSeconds);

    private void Advance()
    {
        _clickIndex++;
        if (_clickIndex >= _demo.Tiles.Count)
        {
            _clickIndex = 0;
            _clickLoop++;
        }
    }

    /// <summary>
    /// 生成一声短促的「嗒」：2 kHz 正弦，指数衰减，30 ms。
    /// 用读取回调填数据，不用 SetData：Unity 6 的 SetData 有 ReadOnlySpan 重载，net48 编译环境里没有这个类型。
    /// </summary>
    private static AudioClip MakeClick()
    {
        const int rate = 44100;
        const int length = (int)(rate * 0.03);
        var position = 0;
        return AudioClip.Create("RhythmHintClick", length, 1, rate, false,
            data =>
            {
                for (var i = 0; i < data.Length; i++)
                {
                    var t = (position + i) / (float)rate;
                    data[i] = position + i < length ? Mathf.Sin(2 * Mathf.PI * 2000 * t) * Mathf.Exp(-t * 160) * 0.8f : 0f;
                }

                position += data.Length;
            },
            newPosition => position = newPosition);
    }
}
