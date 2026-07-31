using System.Windows.Input;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;

namespace MYTC.App.Shortcuts;

public sealed class ShortcutManager(IShortcutStore store)
{
    private static readonly TimeSpan SequenceTimeout = TimeSpan.FromMilliseconds(1500);
    private readonly List<string> _pendingChords = [];
    private ShortcutConfiguration _configuration =
        new(ShortcutConfiguration.CurrentSchemaVersion, []);
    private DateTime _lastChordAtUtc;

    public IReadOnlyList<ShortcutBinding> Bindings => _configuration.Bindings;

    public static IReadOnlyList<ShortcutBinding> DefaultBindings =>
        ShortcutDefaults.Create().Bindings;

    public async Task InitializeAsync()
    {
        var loaded = await store.LoadAsync();
        try
        {
            Validate(loaded);
            _configuration = loaded;
        }
        catch (ArgumentException)
        {
            _configuration = ShortcutDefaults.Create();
        }
    }

    public async Task SaveAsync(IReadOnlyList<ShortcutBinding> bindings)
    {
        var configuration = new ShortcutConfiguration(
            ShortcutConfiguration.CurrentSchemaVersion,
            bindings);
        Validate(configuration);
        await store.SaveAsync(configuration);
        _configuration = configuration;
        ResetSequence();
    }

    public Task<IReadOnlyList<string>> ListSchemeNamesAsync()
    {
        return store.ListSchemeNamesAsync();
    }

    public async Task<IReadOnlyList<ShortcutBinding>?> LoadSchemeAsync(string name)
    {
        var configuration = await store.LoadSchemeAsync(name);
        if (configuration is null)
        {
            return null;
        }

        Validate(configuration);
        return configuration.Bindings;
    }

    public async Task SaveSchemeAsync(
        string name,
        IReadOnlyList<ShortcutBinding> bindings)
    {
        var configuration = new ShortcutConfiguration(
            ShortcutConfiguration.CurrentSchemaVersion,
            bindings);
        Validate(configuration);
        await store.SaveSchemeAsync(name, configuration);
    }

    public ShortcutMatch Process(KeyEventArgs e)
    {
        var chord = FormatKeyEvent(e);
        if (chord is null)
        {
            return ShortcutMatch.None;
        }

        if (DateTime.UtcNow - _lastChordAtUtc > SequenceTimeout)
        {
            _pendingChords.Clear();
        }

        _lastChordAtUtc = DateTime.UtcNow;
        _pendingChords.Add(chord);
        var candidate = string.Join(", ", _pendingChords);
        var result = Match(candidate);
        if (result.Handled)
        {
            if (!result.Waiting)
            {
                ResetSequence();
            }

            return result;
        }

        _pendingChords.Clear();
        _pendingChords.Add(chord);
        result = Match(chord);
        if (!result.Handled || !result.Waiting)
        {
            ResetSequence();
        }

        return result;
    }

    public bool IsExactBinding(
        KeyEventArgs e,
        ShortcutAction action)
    {
        var chord = FormatKeyEvent(e);
        return chord is not null &&
            _configuration.Bindings.Any(binding =>
                binding.Action == action &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    NormalizeGesture(binding.Gesture),
                    chord));
    }

    public void ResetSequence()
    {
        _pendingChords.Clear();
        _lastChordAtUtc = default;
    }

    public static string NormalizeGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            gesture.Split(',', StringSplitOptions.TrimEntries)
                .Select(NormalizeChord));
    }

    public static void Validate(ShortcutConfiguration configuration)
    {
        var normalized = configuration.Bindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Gesture))
            .Select(binding => new
            {
                binding.Action,
                Gesture = NormalizeGesture(binding.Gesture),
            })
            .ToArray();

        var duplicate = normalized
            .GroupBy(item => item.Gesture, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"快捷键“{duplicate.Key}”被重复使用。");
        }

        for (var leftIndex = 0; leftIndex < normalized.Length; leftIndex++)
        {
            for (var rightIndex = 0; rightIndex < normalized.Length; rightIndex++)
            {
                if (leftIndex != rightIndex &&
                    normalized[rightIndex].Gesture.StartsWith(
                        normalized[leftIndex].Gesture + ", ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"“{normalized[leftIndex].Gesture}”既是完整快捷键，又是“{normalized[rightIndex].Gesture}”的前缀。");
                }
            }
        }
    }

    public static string GetActionLabel(ShortcutAction action)
    {
        return action switch
        {
            ShortcutAction.CopyToTarget => "复制到目标窗格",
            ShortcutAction.MoveToTarget => "移动到目标窗格",
            ShortcutAction.CreateDirectory => "新建文件夹",
            ShortcutAction.RecycleDelete => "移到回收站",
            ShortcutAction.PermanentDelete => "永久删除",
            ShortcutAction.Rename => "重命名",
            ShortcutAction.CopyToClipboard => "复制到剪贴板",
            ShortcutAction.CutToClipboard => "剪切到剪贴板",
            ShortcutAction.PasteFromClipboard => "从剪贴板粘贴",
            ShortcutAction.ActivatePane1 => "激活左上窗格",
            ShortcutAction.ActivatePane2 => "激活右上窗格",
            ShortcutAction.ActivatePane3 => "激活左下窗格",
            ShortcutAction.ActivatePane4 => "激活右下窗格",
            ShortcutAction.NewTab => "新建标签",
            ShortcutAction.CloseTab => "关闭当前标签",
            ShortcutAction.RestoreClosedTab => "恢复关闭的标签",
            ShortcutAction.RestoreFourPanes => "恢复四窗格",
            ShortcutAction.FocusAddressBar => "激活当前地址栏",
            ShortcutAction.NavigateUp => "上级目录",
            ShortcutAction.ShowProperties => "属性",
            _ => action.ToString(),
        };
    }

    private ShortcutMatch Match(string candidate)
    {
        var exact = _configuration.Bindings.FirstOrDefault(binding =>
            !string.IsNullOrWhiteSpace(binding.Gesture) &&
            StringComparer.OrdinalIgnoreCase.Equals(
                NormalizeGesture(binding.Gesture),
                candidate));
        if (exact is not null)
        {
            return new ShortcutMatch(true, false, exact.Action, string.Empty);
        }

        var isPrefix = _configuration.Bindings.Any(binding =>
            !string.IsNullOrWhiteSpace(binding.Gesture) &&
            NormalizeGesture(binding.Gesture).StartsWith(
                candidate + ", ",
                StringComparison.OrdinalIgnoreCase));
        return isPrefix
            ? new ShortcutMatch(true, true, null, $"连续按键：{candidate} …")
            : ShortcutMatch.None;
    }

    private static string NormalizeChord(string chord)
    {
        var parts = chord.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        string? keyText = null;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
            }
            else if (keyText is null)
            {
                keyText = part;
            }
            else
            {
                throw new ArgumentException($"无法识别快捷键“{chord}”。");
            }
        }

        if (keyText is null)
        {
            throw new ArgumentException($"快捷键“{chord}”缺少按键。");
        }

        var key = ParseKey(keyText);
        return FormatChord(modifiers, key);
    }

    private static Key ParseKey(string text)
    {
        if (text.Length == 1 && char.IsDigit(text[0]))
        {
            return (Key)((int)Key.D0 + (text[0] - '0'));
        }

        if (text.Equals("Esc", StringComparison.OrdinalIgnoreCase))
        {
            return Key.Escape;
        }

        if (text.Equals("Del", StringComparison.OrdinalIgnoreCase))
        {
            return Key.Delete;
        }

        if (text.Equals("Backspace", StringComparison.OrdinalIgnoreCase))
        {
            return Key.Back;
        }

        if (!Enum.TryParse<Key>(text, ignoreCase: true, out var key) ||
            key is Key.None or
                Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or
                Key.LWin or Key.RWin)
        {
            throw new ArgumentException($"无法识别按键“{text}”。");
        }

        return key;
    }

    public static string? FormatKeyEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            return null;
        }

        return FormatChord(Keyboard.Modifiers, key);
    }

    public static string FormatChord(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 =>
                ((int)key - (int)Key.D0).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            Key.Escape => "Esc",
            Key.Delete => "Del",
            Key.Back => "Backspace",
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }
}

public sealed record ShortcutMatch(
    bool Handled,
    bool Waiting,
    ShortcutAction? Action,
    string StatusText)
{
    public static ShortcutMatch None { get; } = new(false, false, null, string.Empty);
}
