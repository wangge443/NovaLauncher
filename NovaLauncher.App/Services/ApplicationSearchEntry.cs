using System.Text;
using ToolGood.Words.Pinyin;

namespace NovaLauncher.App.Services;

public sealed record ApplicationSearchEntry(Guid ApplicationId, string OriginalName,
    string NormalizedName, string FullPinyin, string PinyinInitials)
{
    public static ApplicationSearchEntry Create(Guid id, string name)
    {
        string normalized = Normalize(name);
        if (!name.Any(c => c is >= '\u3400' and <= '\u9fd5'))
            return new(id, name, normalized, normalized, normalized);
        var full = new StringBuilder();
        var initials = new StringBuilder();
        // One offline conversion produces both keys; default phrase readings are kept.
        foreach (string syllable in WordsHelper.GetPinyinList(name, false))
        {
            full.Append(syllable);
            if (syllable.Length > 0)
                initials.Append(syllable[0] <= 127 ? syllable[..1] : syllable);
        }
        return new(id, name, normalized, Normalize(full.ToString()), Normalize(initials.ToString()));
    }

    public static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        // Keep punctuation meaningful in C++, .NET, hyphenated names and emoji.
        foreach (char c in value)
            if (!char.IsWhiteSpace(c)) result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }
}
