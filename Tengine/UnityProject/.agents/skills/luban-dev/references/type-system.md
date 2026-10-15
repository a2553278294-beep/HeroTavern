# 类型与序列化

常用源类型为 bool、byte、short、int、long、float、double、string、text、datetime，以及自定义 enum/bean。
**源类型名不保证等于 C# 类型**：尤其 datetime、text、mapper、容器，必须看当前 cs-bin 生成结果。
不要照旧文档将 datetime 一概写成 DateTime，或把 array 解释成固定长度约束。

XML 常用容器表达式：

```xml
<var name="ids" type="list,int"/>
<var name="names" type="array,string"/>
<var name="weights" type="map,string,int"/>
```

容器括号、可空、内嵌 tags 的语法取当前生成器。helper 的 `list<int>` 解析仅是辅助能力，不能证明生成器所有路径都支持相同文本。
给新类型准备最小数据与错误数据，验证生成的代码及二进制读回，尤其嵌套容器、空值、溢出和浮点边界。
枚举别名是数据输入便利，不可随意重编号；flags 必须验证值组合。
Unity 外部类型映射参考真实 `Configs/GameConfig/Defines/builtin.xml` 和 ExternalTypeUtil 模板，不在游戏代码新建同名类型替代。
