# 非 Excel 数据源

当前 JSON loader 支持单记录对象与多记录数组。表的 map/list 是索引语义，不直接决定 JSON 根类型。
多记录数组需在 input 上使用 `*@rows.json`；普通 `rows.json` 会按单记录读取。
已执行的形状参考 [rows.json](../examples/regression/Datas/rows.json) 和 [tables.xml](../examples/regression/Defines/tables.xml)。

```json
[
  {"id": 1, "groupId": 1, "label": "first"},
  {"id": 2, "groupId": 1, "label": "second"}
]
```

不要把 map 表直接写成主键到行的 JSON 对象，除非相应 loader/选择器明确支持。
嵌套属性、Bean 多态标识、map 容器和值类型需查当前 DataCreator，不把不同格式的标识符混用。
CSV、XML、YAML、Lua、lite 各有 loader 和分隔规则；本项目回归不是这些格式的全覆盖保证。
新增数据源先补最小成功与失败 fixture，再接生产表，不保留缺失输入文件的“完整示例”。
