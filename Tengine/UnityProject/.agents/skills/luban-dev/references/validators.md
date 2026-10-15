# 校验层级

1. helper：表结构、基础值、Bean/枚举引用的预检。
2. Luban：实际 Schema、主键、类型、ref/range/size 等生成校验。
3. 产物：期望 C#、bytes、分片/index 集、模板复制。
4. 运行：对应代码/数据组合加载、边界和资源归属。

所有层级都有自己的证据；前一层通过不替代后一层。
常用约束如 `int#range=[1,99]`、`string#ref=TbItem` 必须配套真实目标表和数据验证。
容器约束与元素约束不要混淆。高级 tag 的语法以当前 Luban 实现为准，不提供跳过校验的生产捷径。
ref 的运行时成员名和 ResolveRef 时机受生成模板影响；标准构造与懒加载/分片不能共用假定。
YooAsset 地址是否可加载必须回到项目收集器和 Editor 验证，不能把通用 path validator 当作 Addressables/YooAsset 等价验证。
field 分片目标的引用限制见 [集成](tengine-integration.md)。
