using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>入场 / 出场动画和设置页演示谱。</summary>
public class PresentationTests
{
    [Fact]
    public void Presence_SlidesInAndOutOverItsDuration()
    {
        var p = new PresenceAnimator(0.4);
        Assert.Equal(0, p.Value);

        p.Update(show: true, 0.2);
        Assert.InRange(p.Value, 0.8, 0.9); // 缓出：前半段走得快（1-(1-0.5)^3 = 0.875）

        p.Update(show: true, 0.3);
        Assert.Equal(1, p.Value);

        p.Update(show: false, 0.2);
        Assert.InRange(p.Value, 0.1, 0.2); // 出场也是缓出：一开始就走得快，不在原地停一下

        p.Update(show: false, 1.0);
        Assert.Equal(0, p.Value);
        Assert.False(p.IsVisible);
    }

    [Fact]
    public void Presence_ExitStartsMovingRightAway()
    {
        // 复现：按 Esc 暂停后面板在原地停约 0.2 秒才开始滑走（出场是入场曲线倒着走，开头几乎不动）
        var p = new PresenceAnimator(0.45);
        p.Update(show: true, 1.0);

        p.Update(show: false, 0.1);
        Assert.True(p.Value < 0.6, $"出场 0.1 秒后还在 {p.Value:0.00}");
    }

    [Fact]
    public void Presence_ReversingMidwayContinuesFromWhereItIs()
    {
        var p = new PresenceAnimator(0.4);
        p.Update(show: true, 1.0);
        p.Update(show: false, 0.1);
        var before = p.Value;

        // 刚暂停又马上继续：从当前位置往回走，不跳
        p.Update(show: true, 0.001);
        Assert.InRange(p.Value, before, before + 0.02);

        p.Update(show: true, 1.0);
        Assert.Equal(1, p.Value);
    }

    [Fact]
    public void DemoChart_ShowsEveryKindOfNote()
    {
        var demo = DemoChart.Build(160);
        var notes = NotePlanner.Plan(demo.Tiles, new FingeringOptions());

        Assert.Equal(16 * 60.0 / 160, demo.LoopSeconds, 6);
        Assert.Contains(notes, n => n.Lanes.Count == 2);                // 伪双押画成双押
        Assert.Contains(notes, n => n.IsMultiChord);                    // 三押（紫色）
        Assert.Contains(notes, n => n.Lanes.Values.Any(h => h > 0));    // 长按
        Assert.All(demo.Tiles, t => Assert.False(double.IsNaN(t.Angle))); // 和游戏里一样按转角判断多押
        Assert.All(DemoChart.Repeat(demo, 2), t => Assert.False(double.IsNaN(t.Angle)));
        Assert.Equal(4, notes.SelectMany(n => n.Lanes.Keys).Distinct().Count()); // 四条轨道都用到
        Assert.All(demo.Tiles, t => Assert.InRange(t.Time, 0, demo.LoopSeconds));
    }

    [Theory]
    [InlineData(150)]
    [InlineData(160)]
    [InlineData(200)]
    public void SettingsDemo_SameShortRunShowsTheSelectedFingeringStyle(double bpm)
    {
        var regular = DemoChart.BuildForSettings(bpm, FingeringStyle.TimeReset, 4);
        var experimental = DemoChart.BuildForSettings(bpm, FingeringStyle.ReviewPreferenceV1, 4);

        Assert.Equal(regular.Tiles.Select(hit => hit.Time), experimental.Tiles.Select(hit => hit.Time));
        Assert.Contains(regular.Tiles, hit => hit.HoldSeconds > 0);
        Assert.All(experimental.Tiles, hit => Assert.Equal(0, hit.HoldSeconds));
        var start = experimental.SecondsPerBeat * 8.5;
        var end = experimental.SecondsPerBeat * 10;
        var timeReset = NotePlanner.Plan(regular.Tiles, new FingeringOptions()).Where(note => note.Time >= start && note.Time < end).ToArray();
        var preview = NotePlanner.Plan(experimental.Tiles, new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1 })
            .Where(note => note.Time >= start && note.Time < end).ToArray();

        Assert.Equal(4, timeReset.Length);
        Assert.Equal(timeReset.Select(note => note.Time), preview.Select(note => note.Time));
        Assert.Contains(timeReset, note => note.Lanes.ContainsKey(0) || note.Lanes.ContainsKey(3));
        Assert.Equal(new[] { 2, 1, 2, 1 }, preview.Select(note => Assert.Single(note.Lanes.Keys)));
    }

    [Theory]
    [InlineData(150)] // 用户截图时的主菜单音乐
    [InlineData(160)]
    public void SettingsDemo_VeryFastRunOnlyForSixAndEightKeys(double bpm)
    {
        // 复现（2026-11-26 用户截图）：4K 演示第 13 拍起挤成一段 20 ms 一下的密楼梯，和 1.3.5 不一样；这段只为 6K、8K 看得出多指轮
        static TileHit[] Run(DemoChart demo) =>
            demo.Tiles.Where(t => t.Time >= (demo.SecondsPerBeat * 12) - 1e-9 && t.Time < demo.SecondsPerBeat * 14).ToArray();

        var four = Run(DemoChart.BuildForSettings(bpm, FingeringStyle.TimeReset, 4));
        Assert.Equal(16, four.Length);
        Assert.All(four.Zip(four.Skip(1)), p => Assert.Equal(60 / bpm / 8, p.Second.Time - p.First.Time, 6));

        foreach (var keys in new[] { 6, 8 })
        {
            var many = Run(DemoChart.BuildForSettings(bpm, FingeringStyle.TimeReset, keys));
            Assert.Equal(18, many.Length);
            Assert.Equal(9, many.Zip(many.Skip(1)).Count(p => Math.Abs(p.Second.Time - p.First.Time - 0.02) < 1e-6));
        }
    }

    [Fact]
    public void Holds_ActiveFromPressUntilRelease()
    {
        // 复现：长按音符按下后就消失，没有按住中的动画
        var hold = new PlannedNote(1, 1.0);
        hold.Lanes[2] = 0.5;
        var tap = new PlannedNote(2, 1.2);
        tap.Lanes[1] = 0;
        var notes = new[] { hold, tap };

        Assert.Empty(Holds.Active(notes, nextNote: 0, now: 0.9));   // 还没按到

        var active = Holds.Active(notes, nextNote: 2, now: 1.3);    // 按住中，普通音符不算
        var h = Assert.Single(active);
        Assert.Equal(2, h.Lane);
        Assert.Equal(0.2, h.Remaining, 6);
        Assert.Equal(0.6, h.Progress, 6);

        Assert.Empty(Holds.Active(notes, nextNote: 2, now: 1.6));   // 已松开
    }

    [Fact]
    public void FloorHits_HoldEndIsNotAPress()
    {
        // 第 2、3 块是连着的长按：第 3 块要重新按一下，各自按住到下一块（2026-11-20 用户录屏：原来合成一条，照提示按住会死）；
        // 第 4 块是长按结束（松开时落上），第 5 块要按；第 6 块是中旋
        var floors = new[]
        {
            new FloorInfo(0, 0.0, false, false),
            new FloorInfo(1, 0.5, false, false),
            new FloorInfo(2, 1.0, true, false),
            new FloorInfo(3, 1.5, true, false),
            new FloorInfo(4, 2.0, false, false),
            new FloorInfo(5, 2.5, false, false),
            new FloorInfo(6, 3.0, false, true),
        };

        var hits = FloorHits.From(floors);

        Assert.Equal(new[] { 1, 2, 3, 5 }, hits.Select(h => h.TileIndex));
        Assert.Equal(0.5, hits[1].HoldSeconds, 6); // 第 2 块按住到第 3 块
        Assert.Equal(0.5, hits[2].HoldSeconds, 6); // 第 3 块重新按下，按住到第 4 块
        Assert.Equal(0, hits[3].HoldSeconds);
    }

    [Theory]
    [InlineData(150)] // 用户截图时的主菜单音乐
    [InlineData(160)]
    [InlineData(200)]
    public void DemoChart_HasNoLoneNotes(double bpm)
    {
        // 复现：演示里 1/8 拍滚键结尾 …2 3 0 后接右手双押，左边最外侧的 0 孤零零一个
        var demo = DemoChart.Build(bpm);
        var tiles = DemoChart.Repeat(demo, 3);
        var lone = LayoutStats.LoneNotes(NotePlanner.Plan(tiles, new FingeringOptions()), LayoutStats.DefaultLinkGap);

        Assert.True(lone.Count == 0, string.Join("；", lone.Select(n => $"{n.Time:F3}s 轨道 {string.Join("+", n.Lanes.Keys)}")));
    }

    [Fact]
    public void Viewport_ShrinksIntoAreaLeftByPanels()
    {
        var full = ViewportMath.Shrink(new ViewRect(0, 0, 1, 1), left: 0.15, top: 0.12);
        Assert.Equal(0.15, full.X, 6);
        Assert.Equal(0, full.Y, 6);              // Unity 的显示区域从左下角算，顶部让出来就是高度变小
        Assert.Equal(0.85, full.Width, 6);
        Assert.Equal(0.88, full.Height, 6);

        // 游戏自己有上下黑边（只用中间 80%）时，也按比例缩进同一块区域
        var letterboxed = ViewportMath.Shrink(new ViewRect(0, 0.1, 1, 0.8), left: 0.2, top: 0.1);
        Assert.Equal(0.2, letterboxed.X, 6);
        Assert.Equal(0.09, letterboxed.Y, 6);
        Assert.Equal(0.8, letterboxed.Width, 6);
        Assert.Equal(0.72, letterboxed.Height, 6);

        // 面板完全收起时不变
        Assert.Equal(new ViewRect(0, 0, 1, 1), ViewportMath.Shrink(new ViewRect(0, 0, 1, 1), 0, 0));
    }

    private sealed class FakeCamera
    {
        public ViewRect Rect = new(0, 0, 1, 1);
        public bool ToTexture;
    }

    private static ViewportShrinker<FakeCamera> Shrinker() =>
        new(c => c.Rect, (c, r) => c.Rect = r, c => c.ToTexture, _ => true);

    [Fact]
    public void Viewport_RestoresCameraThatSwitchesToRenderTexture()
    {
        // 复现：编辑器里摄像机直接画屏幕，被缩小了；进关卡 / 选关时游戏让它改画到贴图（再整体显示），
        // 它还保持缩小的区域，贴图里其余部分是没清过的显存，屏幕上一大片彩色花屏
        var cam = new FakeCamera();
        var shrinker = Shrinker();
        shrinker.Apply(new[] { cam }, left: 0.2, top: 0.1);
        Assert.Equal(0.2, cam.Rect.X, 6);

        cam.ToTexture = true;
        shrinker.Apply(new[] { cam }, left: 0.2, top: 0.1);

        Assert.Equal(new ViewRect(0, 0, 1, 1), cam.Rect);
    }

    [Fact]
    public void Viewport_RestoresTrackedCameraEvenWhenNotListed()
    {
        // 游戏手动渲染（自定义帧率）时摄像机被禁用，不在 Camera.allCameras 里，但改画贴图后同样要还原
        var cam = new FakeCamera();
        var shrinker = Shrinker();
        shrinker.Apply(new[] { cam }, 0.2, 0.1);

        cam.ToTexture = true;
        shrinker.Apply(Array.Empty<FakeCamera>(), 0.2, 0.1);

        Assert.Equal(new ViewRect(0, 0, 1, 1), cam.Rect);
    }

    [Fact]
    public void Viewport_DoesNotShrinkTwiceAndRespectsGameChanges()
    {
        var cam = new FakeCamera();
        var shrinker = Shrinker();
        shrinker.Apply(new[] { cam }, 0.2, 0.1);
        shrinker.Apply(new[] { cam }, 0.2, 0.1);
        Assert.Equal(0.2, cam.Rect.X, 6);                 // 每帧都从原始区域算，不会越缩越小
        Assert.Equal(0.8, cam.Rect.Width, 6);

        cam.Rect = new ViewRect(0, 0.1, 1, 0.8);          // 游戏自己改了（例如加上下黑边）：以新值为原始值
        shrinker.Apply(new[] { cam }, 0.2, 0.1);
        Assert.Equal(0.09, cam.Rect.Y, 6);

        shrinker.Restore();
        Assert.Equal(new ViewRect(0, 0.1, 1, 0.8), cam.Rect);
    }

    [Fact]
    public void DemoChart_RepeatsWithoutGaps()
    {
        var demo = DemoChart.Build(160);
        var three = DemoChart.Repeat(demo, 3);

        Assert.Equal(demo.Tiles.Count * 3, three.Count);
        Assert.Equal(demo.Tiles[0].Time + demo.LoopSeconds, three[demo.Tiles.Count].Time, 6);
        Assert.Equal(Enumerable.Range(1, three.Count), three.Select(t => t.TileIndex));
    }
}
