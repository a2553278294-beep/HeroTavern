# 刷新、取消与资源释放

`UIWindow` 是普通 C# 对象，不是 `UnityEngine.Object`。`this == null` 无法检测窗口对应 GameObject 的销毁。
`UserData` 是第一个参数，`UserDatas` 是参数数组；先做类型匹配，不盲目强转。

下面是窗口内片段。版本号阻止旧请求写回；finally 释放未转移的资源。
`Render` 是业务实现，需使用真实资源地址替换示例位置。

```csharp
private TextAsset _asset;
private int _revision;
private bool _disposed;
private CancellationTokenSource _load;

protected override void OnRefresh()
{
    int revision = ++_revision;
    _load?.Cancel();
    _load?.Dispose();
    _load = null;
    if (_disposed || UserData is not string location || string.IsNullOrWhiteSpace(location)) return;
    _load = new CancellationTokenSource();
    LoadAsync(location, revision, _load.Token).Forget();
}

private async UniTask LoadAsync(string location, int revision, CancellationToken token)
{
    TextAsset pending = null;
    try
    {
        pending = await GameModule.Resource.LoadAssetAsync<TextAsset>(location, token);
        if (pending == null || token.IsCancellationRequested || _disposed || revision != _revision)
            return;
        TextAsset previous = _asset;
        _asset = pending;
        pending = null;
        if (previous != null) GameModule.Resource.UnloadAsset(previous);
        Render(_asset.text);
    }
    catch (OperationCanceledException) { }
    catch (Exception exception) { Log.Error(exception.ToString()); }
    finally
    {
        if (pending != null) GameModule.Resource.UnloadAsset(pending);
    }
}

protected override void OnDestroy()
{
    _disposed = true;
    ++_revision;
    _load?.Cancel();
    _load?.Dispose();
    _load = null;
    if (_asset != null) GameModule.Resource.UnloadAsset(_asset);
    _asset = null;
}
```

需要 `System`、`System.Threading`、`Cysharp.Threading.Tasks`、`UnityEngine`、`TEngine`。
如果隐藏就应取消，在 `OnSetVisible(false)` 定义单独策略；不要把隐藏等同销毁。
只需读取的数据可在 try/finally 内直接消费，不必长期持有。
并发批量加载中某项失败时，也要回收已经成功的其他项。
音频交给 Audio 模块持有，不要先额外 LoadAsset 再调用 Audio.Play 导致双重加载。

回归至少包括连续刷新、慢请求覆盖快请求、失败、隐藏、销毁与加载完成同帧。
