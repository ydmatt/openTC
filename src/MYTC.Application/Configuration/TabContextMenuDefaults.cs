using MYTC.Domain.Configuration;

namespace MYTC.Application.Configuration;

public static class TabContextMenuDefaults
{
    public static TabContextMenuConfiguration Create()
    {
        return new TabContextMenuConfiguration(
            TabContextMenuConfiguration.CurrentSchemaVersion,
            [
                BuiltIn(
                    "pin-current-directory",
                    "固定当前目录（&P）",
                    TabContextMenuAction.PinCurrentDirectory),
                BuiltIn(
                    "configure",
                    "标签设置…（&S）",
                    TabContextMenuAction.Configure),
                BuiltIn(
                    "copy-to-target",
                    "复制标签到目标窗格（&T）",
                    TabContextMenuAction.CopyToTargetPane),
                Separator("separator-1"),
                BuiltIn(
                    "move-left",
                    "向左移动（&L）",
                    TabContextMenuAction.MoveLeft),
                BuiltIn(
                    "move-right",
                    "向右移动（&R）",
                    TabContextMenuAction.MoveRight),
                Separator("separator-2"),
                BuiltIn(
                    "close",
                    "关闭标签（&C）",
                    TabContextMenuAction.Close),
            ]);
    }

    private static TabContextMenuItemDefinition BuiltIn(
        string id,
        string label,
        TabContextMenuAction action)
    {
        return new TabContextMenuItemDefinition(
            id,
            TabContextMenuItemKind.BuiltIn,
            label,
            action,
            true);
    }

    private static TabContextMenuItemDefinition Separator(string id)
    {
        return new TabContextMenuItemDefinition(
            id,
            TabContextMenuItemKind.Separator,
            string.Empty,
            null,
            true);
    }
}
