using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Luban.DataTarget;
using Luban.Defs;
using Luban.Datas;
using Luban.DataExporter.Builtin.Binary;
using Luban.Serialization;

// Luban's PipelineScope plugin scan loads every *Luban*.dll beside Luban.dll and registers
// assemblies marked with [RegisterBehaviour].
[assembly: Luban.RegisterBehaviour]

namespace Luban.Sharded
{
    /// <summary>
    /// Parsed from table tags: partition=range|count|field, partition_field=&lt;name&gt;, partition_size=&lt;n&gt;.
    /// </summary>
    public sealed class PartitionSpec
    {
        public string Mode;
        public string Field;
        public int Size;

        public static bool IsPartitioned(DefTable table)
        {
            return table.Tags != null && table.Tags.TryGetValue("partition", out var mode) && !string.IsNullOrWhiteSpace(mode);
        }

        public static PartitionSpec Parse(DefTable table)
        {
            var tags = table.Tags;
            string mode = tags["partition"].Trim();
            if (mode != "range" && mode != "count" && mode != "field")
            {
                throw new ArgumentException($"table {table.FullName}: unknown partition mode '{mode}' (expected range|count|field)");
            }
            if (!tags.TryGetValue("partition_field", out var field) || string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException($"table {table.FullName}: partition requires partition_field");
            }
            int size = 0;
            if (mode != "field")
            {
                if (!tags.TryGetValue("partition_size", out var raw) || !int.TryParse(raw, out size) || size <= 0)
                {
                    throw new ArgumentException($"table {table.FullName}: partition={mode} requires a positive partition_size");
                }
            }
            return new PartitionSpec { Mode = mode, Field = field.Trim(), Size = size };
        }
    }

    /// <summary>
    /// bin-sharded: like bin, but tables tagged with partition=... are split into multiple
    /// &lt;output&gt;__p_*.bytes files. Non-partitioned tables behave exactly like bin.
    ///
    /// Naming contract (asserted by the workflow regression):
    ///   range: &lt;output&gt;__p_&lt;shard&gt;.bytes          shard = floor(field / partition_size)
    ///   count: &lt;output&gt;__p_&lt;NNNN&gt;.bytes          fixed partition_size records per shard, 4-digit index
    ///          plus &lt;output&gt;__index.bytes          WriteSize(total); per record: index fields (binary) + WriteInt(shard)
    ///   field: &lt;output&gt;__p_&lt;value&gt;.bytes         one shard per distinct partition_field value
    /// </summary>
    [DataTarget("bin-sharded")]
    public class ShardedBinaryDataTarget : BinaryDataTarget
    {
        private static void WriteRecords(List<Record> records, ByteBuf buf)
        {
            buf.WriteSize(records.Count);
            foreach (var record in records)
            {
                record.Data.Apply(BinaryDataVisitor.Ins, buf);
            }
        }

        private static object FieldValue(Record record, string field)
        {
            var data = record.Data.GetField(field);
            return data switch
            {
                DInt x => x.Value,
                DLong x => x.Value,
                DString x => x.Value,
                DEnum x => x.Value,
                _ => throw new NotSupportedException($"partition field type {data.GetType().Name} is not supported"),
            };
        }

        private OutputFile ShardFile(string baseName, string suffix, List<Record> records)
        {
            var buf = new ByteBuf();
            WriteRecords(records, buf);
            return CreateOutputFile($"{baseName}__p_{suffix}.{OutputFileExt}", buf.CopyData());
        }

        /// <summary>Export one partitioned table as shard files (plus __index for count mode).</summary>
        public List<OutputFile> ExportSharded(DefTable table, List<Record> records, PartitionSpec spec)
        {
            string baseName = table.OutputDataFile;
            var files = new List<OutputFile>();
            switch (spec.Mode)
            {
                case "range":
                {
                    foreach (var group in records.GroupBy(r => Convert.ToInt64(FieldValue(r, spec.Field)) / spec.Size).OrderBy(g => g.Key))
                    {
                        files.Add(ShardFile(baseName, group.Key.ToString(), group.ToList()));
                    }
                    break;
                }
                case "count":
                {
                    var index = new ByteBuf();
                    index.WriteSize(records.Count);
                    for (int i = 0; i < records.Count; i += spec.Size)
                    {
                        int shard = i / spec.Size;
                        files.Add(ShardFile(baseName, shard.ToString("D4"), records.Skip(i).Take(spec.Size).ToList()));
                    }
                    for (int i = 0; i < records.Count; i++)
                    {
                        foreach (var indexInfo in table.IndexList)
                        {
                            records[i].Data.GetField(indexInfo.IndexField.Name).Apply(BinaryDataVisitor.Ins, index);
                        }
                        index.WriteInt(i / spec.Size);
                    }
                    files.Add(CreateOutputFile($"{baseName}__index.{OutputFileExt}", index.CopyData()));
                    break;
                }
                case "field":
                {
                    foreach (var group in records.GroupBy(r => FieldValue(r, spec.Field).ToString()).OrderBy(g => g.Key, StringComparer.Ordinal))
                    {
                        files.Add(ShardFile(baseName, group.Key, group.ToList()));
                    }
                    break;
                }
            }
            return files;
        }
    }

    /// <summary>
    /// dataExporter=sharded: partitioned tables (paired with a bin-sharded target) emit shard files;
    /// every other table is exported exactly like the default per-table exporter.
    /// </summary>
    [DataExporter("sharded")]
    public class ShardedDataExporter : DataExporterBase
    {
        public override void Handle(GenerationContext ctx, IDataTarget dataTarget, OutputFileManifest manifest)
        {
            List<DefTable> tables = dataTarget.ExportAllRecords ? ctx.Tables : ctx.ExportTables;
            var tasks = tables.Select(table => Task.Run(() =>
            {
                var records = ctx.GetTableExportDataList(table);
                if (dataTarget is ShardedBinaryDataTarget sharded && PartitionSpec.IsPartitioned(table))
                {
                    foreach (var file in sharded.ExportSharded(table, records, PartitionSpec.Parse(table)))
                    {
                        manifest.AddFile(file);
                    }
                }
                else
                {
                    manifest.AddFile(dataTarget.ExportTable(table, records));
                }
            })).ToArray();
            Task.WaitAll(tasks);
        }
    }
}
