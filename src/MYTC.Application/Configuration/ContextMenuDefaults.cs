using MYTC.Domain.Configuration;

namespace MYTC.Application.Configuration;

public static class ContextMenuDefaults
{
    public static ContextMenuConfiguration Create()
    {
        return new ContextMenuConfiguration(
            ContextMenuConfiguration.CurrentSchemaVersion,
            [
                BuiltIn("open", "打开", ContextMenuAction.Open),
                BuiltIn("open-with", "打开方式（&H）", ContextMenuAction.OpenWith),
                Submenu("new-submenu", "新建（&W）"),
                BuiltIn(
                    "create-directory",
                    "文件夹（&F）",
                    ContextMenuAction.CreateDirectory,
                    "new-submenu"),
                BuiltIn(
                    "create-text-document",
                    "文本文档（.txt）（&T）",
                    ContextMenuAction.CreateTextDocument,
                    "new-submenu"),
                Separator("separator-1"),
                BuiltIn("copy-target", "复制到目标窗格", ContextMenuAction.CopyToTarget),
                BuiltIn("move-target", "移动到目标窗格", ContextMenuAction.MoveToTarget),
                Separator("separator-2"),
                BuiltIn("copy", "复制", ContextMenuAction.CopyToClipboard),
                BuiltIn("cut", "剪切", ContextMenuAction.CutToClipboard),
                BuiltIn("paste", "粘贴", ContextMenuAction.PasteFromClipboard),
                BuiltIn("copy-full-path", "复制完整路径", ContextMenuAction.CopyFullPath),
                Separator("separator-3"),
                BuiltIn("rename", "重命名", ContextMenuAction.Rename),
                BuiltIn("delete", "移到回收站", ContextMenuAction.RecycleDelete),
                BuiltIn("undo-delete", "撤销删除（&U）", ContextMenuAction.UndoDelete),
                BuiltIn("delete-permanent", "永久删除", ContextMenuAction.PermanentDelete),
                BuiltIn("refresh", "刷新（&E）", ContextMenuAction.Refresh),
                BuiltIn("properties", "属性（&R）", ContextMenuAction.Properties),
            ]);
    }

    private static ContextMenuItemDefinition BuiltIn(
        string id,
        string label,
        ContextMenuAction action,
        string? parentId = null)
    {
        return new ContextMenuItemDefinition(
            id,
            ContextMenuItemKind.BuiltIn,
            label,
            action,
            null,
            null,
            true,
            parentId);
    }

    private static ContextMenuItemDefinition Separator(
        string id,
        string? parentId = null)
    {
        return new ContextMenuItemDefinition(
            id,
            ContextMenuItemKind.Separator,
            string.Empty,
            null,
            null,
            null,
            true,
            parentId);
    }

    private static ContextMenuItemDefinition Submenu(
        string id,
        string label)
    {
        return new ContextMenuItemDefinition(
            id,
            ContextMenuItemKind.Submenu,
            label,
            null,
            null,
            null,
            true);
    }
}
