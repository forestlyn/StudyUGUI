# UGUI 源码学习导航

这组笔记以本项目 `Packages/com.unity.ugui` 的实际源码为依据，解释 UGUI 如何通过组件组合、接口协作和延迟重建组织 UI。

分析日期：2026-10-15。源码包版本：**com.unity.ugui 2.0.0**；项目编辑器版本：**Unity 6000.0.49f1**。包同时包含 UGUI 与 TextMeshPro。版本依据：[package.json](../Packages/com.unity.ugui/package.json)、[ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt)。

## 从这里开始

| 文件 | 解决的问题 |
| --- | --- |
| [01 · 整体架构](UGUI-整体架构.md) | 源码怎样分层？核心类如何协作？引擎和包的边界在哪里？ |
| [02 · 核心运行链路](UGUI-核心运行链路.md) | 一次属性变化怎样变成画面？布局如何计算？点击怎样到达 Button？ |
| [03 · 源码阅读路线](UGUI-源码阅读路线.md) | 先读哪些文件？断点放在哪里？如何用实验验证理解？ |
| [交互式框架图](UGUI-架构图.html) | 在整体架构、类关系、重建、布局、事件、遮罩与文字六个视图之间切换，点击节点查看源码入口与说明 |

HTML 是独立文件，**双击即可离线打开**，没有 CDN、外部字体或联网脚本依赖。图中的实线表示调用／数据传递，虚线表示接口协作／组成关系；每条关系都有具体标签。类关系视图另以箭头标签明确“继承”方向。源码链接指向本项目的相对路径；浏览器可能将 `.cs` 文件作为文本打开或下载，可结合展示的行号在 IDE 中定位。

## 先建立这幅脑图

```text
业务修改组件属性
    ├─ 显示：Graphic → 标脏 → CanvasUpdateRegistry → Mesh / Material → CanvasRenderer
    ├─ 布局：ILayoutElement + ILayoutController → LayoutRebuilder → RectTransform
    └─ 交互：EventSystem → InputModule → Raycaster → ExecuteEvents → 控件 → UnityEvent

贯穿其中：GameObject 组件组合、RectTransform 层级、遮罩、CanvasGroup、生命周期注册。
最终绘制：Unity 引擎底层 Canvas / CanvasRenderer。
```

理解架构时先区分三类关系：**继承**决定共同行为，**同一对象上的组件组合**决定具体控件，**注册表和接口**连接不同子系统。例如 Button 继承 Selectable，通过 `targetGraphic` 使用 Image；Button 不继承 Image，也不负责生成按钮背景的网格。

## 阅读约定与范围

- 所有源码路径均相对本项目，不把其他版本 UGUI 的实现直接套入这份代码。
- 文中源码链接附 `#L行号`，并在重点入口旁写出方法名；行号对应分析时的源码快照，修改后请按方法名定位。
- 图中分层属于对源码职责的归纳，不代表这些层都是独立程序集。
- 本地没有 Canvas、CanvasRenderer、RectTransform、CanvasGroup 的引擎实现；对批处理、底层绘制与原生调度只描述可见边界。
- 本次完成的是源码静态分析和学习材料，未启动 Unity 场景执行运行时实验；实验步骤供你后续验证。

已有的 [Study UGUI.md](Study%20UGUI.md) 保留原样。
