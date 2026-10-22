# UGUI学习

UGUI学习仓库

## EventSystem

从一次 Button 点击出发，学习射线检测、事件派发，以及 ScrollRect 拖拽与点击的关系。

- 学习笔记：[UGUI EventSystem](Docs/EventSystem/UGUI%20EventSystem.md)
- PDF：[UGUI EventSystem.pdf](Docs/EventSystem/UGUI%20EventSystem.pdf)

演示场景位于 `Assets/Scenes/EventSystemStudy/`，在 Unity 中打开后运行：

| Scene | 内容 |
| --- | --- |
| [ClickButtonScene](Assets/Scenes/EventSystemStudy/ClickButtonScene.unity) | 按钮点击：射线命中 TMP、向父级查找处理对象，最终触发 Button.onClick。 |
| [ClickScrollScene](Assets/Scenes/EventSystemStudy/ClickScrollScene.unity) | 点击与拖拽：对比轻点按钮和按住拖动 ScrollRect 的行为。 |

