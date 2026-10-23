namespace NovaLauncher.App.Models;

// Win32 MOD_* and virtual-key values are the persisted contract, not WPF enum ordinals.
public readonly record struct GlobalHotkey(int Modifiers, int Key)
{
    public static GlobalHotkey Default => new(3, 0x20);
    public string? ValidationError => Modifiers <= 0 || (Modifiers & ~15) != 0
        ? "请至少使用 Ctrl、Alt 或 Shift 修饰键。"
        : (Modifiers & 8) != 0 ? "Win 组合键由 Windows 保留，请使用 Ctrl、Alt 或 Shift。"
        : !SupportedKey(Key) ? "请选择字母、数字、功能键或 Space 等有效按键（Esc 和 F12 不可用）。"
        : ((Modifiers & 1) != 0 && Key is 0x73 or 0x09) || ((Modifiers & 3) == 3 && Key == 0x2E)
            ? "此组合由系统保留，请选择其他快捷键。" : null;
    private static bool SupportedKey(int key) => key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A
        or >= 0x70 and <= 0x7A or >= 0x7C and <= 0x87 or 0x20 or 0x0D or 0x09 or 0x08
        or >= 0x21 and <= 0x28 or 0x2D or 0x2E;
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(Key switch
        {
            0x20 => "Space", 0x0D => "Enter", 0x09 => "Tab", 0x08 => "Backspace",
            0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2D => "Insert", 0x2E => "Delete",
            >= 0x70 and <= 0x87 => "F" + (Key - 0x6F),
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)Key).ToString(), _ => "Unknown"
        });
        return string.Join(" + ", parts);
    }
}
