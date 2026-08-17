# openTC 数据模型

状态：第一稿  
日期：2026-07-27

## 1. 配置文件划分

```text
data/
  settings.json              全局设置
  session.json               最近一次运行状态
  session.json.bak           上一次可恢复状态
  workspaces/
    bidding.json             标书工作区
    youtube.json             视频工作区
```

全局设置不随工作区切换：

- 快捷键
- 右键菜单
- 主题
- 窗口级偏好

工作区保存：

- 布局树和窗格比例
- 窗格、标签及顺序
- 标题、当前路径、固定目录
- 排序方式
- 活动窗格和目标窗格

## 2. WorkspaceProfile

```text
WorkspaceProfile
  schemaVersion
  id
  name
  layout
  panes[]
  activePaneId
  targetPaneId
  updatedAt
```

布局从第一版就使用树结构，避免未来自定义布局时推翻四窗格数据格式：

```text
SplitNode(horizontal, 0.5)
  SplitNode(vertical, 0.5)
    PaneNode(top-left)
    PaneNode(bottom-left)
  SplitNode(vertical, 0.5)
    PaneNode(top-right)
    PaneNode(bottom-right)
```

## 3. PaneState

```text
PaneState
  id
  tabs[]
  activeTabId
  lastFocusedAt
```

窗格自身不保存目录，目录属于标签。

## 4. TabState

```text
TabState
  id
  customTitle
  mode                  normal | fixed
  currentPath
  fixedPath
  backHistory[]
  forwardHistory[]
  sort
  createdAt
  lastActivatedAt
```

规则：

- `normal` 标签激活时使用 `currentPath`。
- `fixed` 标签每次点击或激活时把导航目标设置为 `fixedPath`。
- 固定标签临时导航时仍更新内存中的 `currentPath`，但下一次激活会再次使用 `fixedPath`。
- `customTitle` 非空时始终显示，不根据路径变化。
- 路径不可用时保留配置，不自动改写固定目录。

## 5. SortState

```text
SortState
  column                name | modifiedAt | type | size
  direction             ascending | descending
  foldersFirst          true
```

同一列再次点击时切换升降序。切换其他列时默认升序；文件夹优先规则独立保留。

## 6. FileItemView

这是展示模型，不直接持久化：

```text
FileItemView
  fullPath
  name
  kind                  file | directory | drive
  modifiedAt
  typeDisplayName
  size
  attributes
  iconKey
  isSelected
```

文件夹大小第一版不主动递归计算，避免目录列表被阻塞。

## 7. ShortcutBinding

```text
ShortcutBinding
  commandId
  sequences[]

KeySequence
  chords[]

KeyChord
  key
  modifiers[]           Ctrl | Alt | Shift | Win
```

示例：

```json
{
  "commandId": "file.copy-to-target",
  "sequences": [
    { "chords": [{ "key": "F5", "modifiers": [] }] }
  ]
}
```

```json
{
  "commandId": "navigation.open-favorite",
  "sequences": [
    {
      "chords": [
        { "key": "D", "modifiers": ["Ctrl"] },
        { "key": "P", "modifiers": [] }
      ]
    }
  ]
}
```

需要保持不同语义的命令：

- `clipboard.copy`
- `clipboard.cut`
- `clipboard.paste`
- `file.copy-to-target`
- `file.move-to-target`

## 8. ContextMenuItem

```text
ContextMenuItem
  id
  kind                  builtIn | external | separator | submenu
  label
  commandId
  executablePath
  argumentTemplates[]
  workingDirectory
  visible
  order
  children[]
```

第一版参数变量：

- `{currentFile}`：第一个选中文件
- `{selectedFiles}`：所有选中文件，作为独立参数展开
- `{currentDirectory}`：活动标签当前目录
- `{targetDirectory}`：目标窗格当前目录

外部程序启动时使用参数数组，不把整段参数拼成命令行字符串。

## 9. FileOperationRequest

```text
FileOperationRequest
  id
  kind                  copy | move | rename | delete | permanentDelete | createFolder
  sourcePaths[]
  destinationPath
  conflictPolicy        ask | overwrite | skip | autoRename
  requestedAt
  sourcePaneId
  targetPaneId
```

执行结果：

```text
FileOperationResult
  requestId
  status                completed | partial | cancelled | failed
  completedItems[]
  failedItems[]
  wasAborted
  startedAt
  finishedAt
```

## 10. 版本迁移

每个配置文件必须有整数 `schemaVersion`。

加载规则：

1. 当前版本：直接验证后加载。
2. 旧版本：逐级迁移并保存备份。
3. 新于当前程序的版本：只读提示，不覆盖文件。
4. 正式文件损坏：尝试 `.bak`。
5. 两者都损坏：创建默认内存状态，并要求用户决定是否覆盖。
