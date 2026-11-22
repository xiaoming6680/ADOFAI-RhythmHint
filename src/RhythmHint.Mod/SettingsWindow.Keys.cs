using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 设置窗口：按键页（2026-11-22 用户嫌设置臃肿、找不到）。原来「用几键」在排键页、各键数的按键在外观页、打开设置的快捷键在通用页，
/// 两边还要各写一句「去另一页设」；三样都和键盘有关、共用屏幕键盘、互相不能占同一个键，就放到一页里。
/// </summary>
internal sealed partial class SettingsWindow
{
    private bool _tiers; // 「档位分界」展开了没有

    private void KeysPage(Settings s, Overlay overlay)
    {
        PageHeader(Page.Keys, Lang.T("设置游戏中使用的键数、各键数对应的按键，以及打开本窗口的快捷键。"));
        KeyCountCard(s);

        _ui.BeginCard("keys", "keyboard", Lang.T("按键框"));
        _keyPicker.Draw(_ui, s);
        _ui.EndCard();

        _ui.BeginCard("hotkey", "keyboard", Lang.T("快捷键"));
        _hotkeyPicker.Draw(_ui, s);
        _ui.Hint(Lang.T("也可在 Unity Mod Manager（Ctrl+F10）中打开本窗口。"));
        _ui.EndCard();

        ResetFooter(overlay, "keys", () =>
        {
            s.ResetKeys();
            _keyPicker.Close();
            _hotkeyPicker.Close();
        }, Lang.T("使用键数保持不变。"));
    }

    /// <summary>
    /// 键数（2026-11-22 用户：按谱面难度动态选择，玩家可以关掉、改难度区间或者直接选几键）：自动（试验性）或 4K、6K、8K。
    /// 自动时从哪一档起用 6K、8K 收在「档位分界」里，摘要写着现在的两档；演示按几键播在校准页的演示预览里选。
    /// </summary>
    private void KeyCountCard(Settings s)
    {
        _ui.BeginCard("keyCount", "keyboard", Lang.T("键数"));
        var selected = s.AutoKeyCount ? 0 : System.Array.IndexOf(KeyLayout.Modes, s.KeyCountFor(0).Keys) + 1;
        var picked = _ui.Choice("keyCount", "keyboard", Lang.T("使用键数"), s.AutoKeyCount ? Lang.T("根据估算难度自动选择（试验性）") : null, selected,
            Lang.T("自动"), "4K", "6K", "8K");
        if (picked != selected)
        {
            s.AutoKeyCount = picked == 0;
            if (picked > 0)
            {
                s.KeyCount = KeyLayout.Modes[picked - 1];
            }
        }

        // 这一帧画什么在开头定好：点「自动」改的是字段，下一帧才出现档位分界，同一帧里控件数量不变
        if (s.AutoKeyCount && picked == selected)
        {
            var summary = Lang.T("6K：{0}，8K：{1}", TierName(s.SixKeysFromTier), TierName(s.EightKeysFromTier));
            _tiers = FoldHeader("tiers", "gauge", Lang.T("档位分界"), summary, _tiers, divider: true);
            if (_ui.BeginFold("tiers", _tiers))
            {
                s.SixKeysFromTier = _ui.SliderInt("sixFrom", "gauge", Lang.T("6K 起始档位"), null, s.SixKeysFromTier, 1, 61, 1, TierName);
                s.EightKeysFromTier = _ui.SliderInt("eightFrom", "gauge", Lang.T("8K 起始档位"), null, s.EightKeysFromTier, s.SixKeysFromTier, 61, 1, TierName);
                _ui.Hint(Lang.T("估算难度低于 6K 起始档位或无法估算时使用 4K；使用专属手序时，按该手序的键数显示。"), 6);
            }

            _ui.EndFold();
        }

        _ui.Gap(6);
        _ui.Hint(Lang.T("6K、8K 采用独立的排键方式：慢速段以食指为主，快速连打由双手交替轮指，并尽量避免同一手指未经休息连续按键。"));
        _ui.EndCard();
    }

    /// <summary>档位名（P1…U20），61 是不用这种键数。</summary>
    private static string TierName(int tier) => tier >= KeyCountChooser.Never ? Lang.T("不使用") : DifficultyTiers.Name(tier);
}
