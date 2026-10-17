using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using TMPro;
using TEngine;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic
{
    public abstract class UIWindow : UIBase
    {
        #region Propreties

        private SetUISafeFitHelper _setUISafeFitHelper;

        /// <summary>html 烘焙窗口适配状态：设计分辨率、宿主画布 RectTransform 与上次适配时的画布尺寸。</summary>
        private Vector2 _htmlDesignResolution;
        private RectTransform _htmlParentRect;
        private Rect _lastHtmlParentRect;

        private System.Action<UIWindow> _prepareCallback;

        private bool _isCreate = false;

        private GameObject _panel;

        private Canvas _canvas;
        
        public Canvas Canvas => _canvas;

        private Canvas[] _childCanvas;

        private GraphicRaycaster _raycaster;
        
        public GraphicRaycaster GraphicRaycaster => _raycaster;

        private GraphicRaycaster[] _childRaycaster;

        public override UIType Type => UIType.Window;

        /// <summary>
        /// 窗口位置组件。
        /// </summary>
        public override Transform transform => _panel.transform;
        
        /// <summary>
        /// 窗口矩阵位置组件。
        /// </summary>
        public override RectTransform rectTransform => _panel.transform as RectTransform;

        /// <summary>
        /// 窗口的实例资源对象。
        /// </summary>
        public override GameObject gameObject => _panel;

        /// <summary>
        /// 窗口名称。
        /// </summary>
        public string WindowName { private set; get; }

        /// <summary>
        /// 窗口层级。
        /// </summary>
        public int WindowLayer { private set; get; }

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string AssetName { private set; get; }

        /// <summary>
        /// 是否为全屏窗口。
        /// </summary>
        public virtual bool FullScreen { private set; get; } = false;

        /// <summary>
        /// 是内部资源无需AB加载。
        /// </summary>
        public bool FromResources { private set; get; }
        
        /// <summary>
        /// 隐藏窗口关闭时间。
        /// </summary>
        public int HideTimeToClose { get; set; }
        
        public int HideTimerId { get; set; }

        /// <summary>
        /// 窗口深度值。
        /// </summary>
        public int Depth
        {
            get
            {
                if (_canvas != null)
                {
                    return _canvas.sortingOrder;
                }
                else
                {
                    return 0;
                }
            }

            set
            {
                if (_canvas != null)
                {
                    if (_canvas.sortingOrder == value)
                    {
                        return;
                    }

                    var oldOrder = _canvas.sortingOrder;
                    // 设置父类
                    _canvas.sortingOrder = value;
                    // 设置子类
                    // int depth = value;
                    for (int i = 0; i < _childCanvas.Length; i++)
                    {
                        var canvas = _childCanvas[i];
                        if (canvas != _canvas)
                        {
                            // depth += 5; //注意递增值
                            // canvas.sortingOrder = depth;
                            canvas.sortingOrder = value + (canvas.sortingOrder - oldOrder);
                        }
                    }


                    // 虚函数
                    if (Visible)
                    {
                        _OnSortDepth();
                    }
                    else
                    {
                        _isSortingOrderDirty = true;
                    }
                }
            }
        }

        /// <summary>
        /// 窗口可见性。
        /// </summary>
        public bool Visible
        {
            get
            {
                if (_canvas != null)
                {
                    return _canvas.gameObject.layer == UIModule.WINDOW_SHOW_LAYER;
                }
                else
                {
                    return false;
                }
            }

            set
            {
                if (_canvas != null)
                {
                    int setLayer = value ? UIModule.WINDOW_SHOW_LAYER : UIModule.WINDOW_HIDE_LAYER;
                    if (_canvas.gameObject.layer == setLayer)
                        return;

                    // 显示设置
                    _canvas.gameObject.layer = setLayer;
                    for (int i = 0; i < _childCanvas.Length; i++)
                    {
                        _childCanvas[i].gameObject.layer = setLayer;
                    }

                    if (value && _isCreate)
                    {
                        _isSortingOrderDirty = false;
                        _OnSortDepth();
                    }

                    // 交互设置
                    Interactable = value;

                    // 虚函数
                    if (_isCreate)
                    {
                        OnSetVisible(value);
                    }
                }
            }
        }

        /// <summary>
        /// 窗口交互性。
        /// </summary>
        private bool Interactable
        {
            get
            {
                if (_raycaster != null)
                {
                    return _raycaster.enabled;
                }
                else
                {
                    return false;
                }
            }

            set
            {
                if (_raycaster != null)
                {
                    _raycaster.enabled = value;
                    for (int i = 0; i < _childRaycaster.Length; i++)
                    {
                        _childRaycaster[i].enabled = value;
                    }
                }
            }
        }

        /// <summary>
        /// 是否加载完毕。
        /// </summary>
        internal bool IsLoadDone = false;
        
        /// <summary>
        /// UI是否销毁。
        /// </summary>
        internal bool IsDestroyed = false;
        
        /// <summary>
        /// UI是否隐藏标志位。
        /// </summary>
        public bool IsHide { internal set; get; } = false;

        #endregion

        public void Init(string name, int layer, bool fullScreen, string assetName, bool fromResources, int hideTimeToClose)
        {
            WindowName = name;
            WindowLayer = layer;
            FullScreen = fullScreen;
            AssetName = assetName;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
        }

        #region 刘海屏适配

        /// <summary>
        /// 移动设备屏幕适配
        /// </summary>
        /// <param name="fitRect">适配的RectTransform对象</param>
        /// <param name="liuHaiFit">是否开启刘海屏顶部适配</param>
        /// <param name="topSpacing">刘海屏顶部适配偏移高度</param>
        /// <param name="bottomFit">是否开启刘海屏底部适配</param>
        /// <param name="bottomSpacing">刘海屏底部适配偏移高度</param>
        public void SetUIFit(RectTransform fitRect, bool liuHaiFit = true, float topSpacing = 0, bool bottomFit = true, float bottomSpacing = 0)
        {
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper(fitRect, liuHaiFit, topSpacing, bottomFit, bottomSpacing);
            }
            _setUISafeFitHelper?.SetUIFit();
        }

        /// <summary>
        /// rectTransform不受m_curRect适配影响
        /// </summary>
        /// <param name="rect"></param>
        public void SetUINotFit(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            _setUISafeFitHelper?.SetUINotFit(rect);
        }

        /// <summary>
        /// 设置某一个节点不受指定RectTransform的影响
        /// </summary>
        /// <param name="rect">设置的RectTransform</param>
        /// <param name="refRect">依赖的RectTransform</param>
        public void SetUINotFit(RectTransform rect, RectTransform refRect)
        {
            if (rect == null || refRect == null)
            {
                return;
            }
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper();
            }
            _setUISafeFitHelper?.SetUINotFit(rect, refRect);
        }

        #endregion

        internal void TryInvoke(System.Action<UIWindow> prepareCallback, System.Object[] userDatas)
        {
            CancelHideToCloseTimer();
            base._userDatas = userDatas;
            if (IsPrepare)
            {
                prepareCallback?.Invoke(this);
            }
            else
            {
                _prepareCallback = prepareCallback;
            }
        }

        internal async UniTaskVoid InternalLoad(string location, Action<UIWindow> prepareCallback, bool isAsync, System.Object[] userDatas)
        {
            _prepareCallback = prepareCallback;
            this._userDatas = userDatas;
            if (!FromResources)
            {
                if (isAsync)
                {
                    var uiInstance = await UIModule.Resource.LoadGameObjectAsync(location, parent: UIModule.UIRoot);
                    Handle_Completed(uiInstance);
                }
                else
                {
                    var uiInstance = UIModule.Resource.LoadGameObject(location, parent: UIModule.UIRoot);
                    Handle_Completed(uiInstance);
                }
            }
            else
            {
                GameObject panel = Object.Instantiate(Resources.Load<GameObject>(location), UIModule.UIRoot);
                Handle_Completed(panel);
            }
        }

        internal void InternalCreate()
        {
            if (_isCreate == false)
            {
                _isCreate = true;
                Inject();
                ScriptGenerator();
                BindMemberProperty();
                RegisterEvent();
                OnCreate();
            }
        }

        internal void InternalRefresh()
        {
            OnRefresh();
        }

        internal bool InternalUpdate()
        {
            if (!IsPrepare || !Visible)
            {
                return false;
            }

            // 画布尺寸变化（窗口拖拽/分辨率切换）时重算烘焙窗口的等比缩放，避免框体脱离。
            if (_htmlParentRect != null && _htmlParentRect.rect != _lastHtmlParentRect)
            {
                FitHtmlBakeWindow();
            }

            List<UIWidget> listNextUpdateChild = null;
            if (ListChild != null && ListChild.Count > 0)
            {
                listNextUpdateChild = _listUpdateChild;
                var updateListValid = _updateListValid;
                List<UIWidget> listChild = null;
                if (!updateListValid)
                {
                    if (listNextUpdateChild == null)
                    {
                        listNextUpdateChild = new List<UIWidget>();
                        _listUpdateChild = listNextUpdateChild;
                    }
                    else
                    {
                        listNextUpdateChild.Clear();
                    }

                    listChild = ListChild;
                }
                else
                {
                    listChild = listNextUpdateChild;
                }

                for (int i = 0; i < listChild.Count; i++)
                {
                    var uiWidget = listChild[i];

                    if (uiWidget == null)
                    {
                        continue;
                    }

                    var needValid = uiWidget.InternalUpdate();

                    if (!updateListValid && needValid)
                    {
                        listNextUpdateChild.Add(uiWidget);
                    }
                }

                if (!updateListValid)
                {
                    _updateListValid = true;
                }
            }

            bool needUpdate = false;
            if (listNextUpdateChild == null || listNextUpdateChild.Count <= 0)
            {
                _hasOverrideUpdate = true;
                OnUpdate();
                needUpdate = _hasOverrideUpdate;
            }
            else
            {
                OnUpdate();
                needUpdate = true;
            }

            return needUpdate;
        }

        internal void InternalDestroy(bool isShutDown = false)
        {
            _isCreate = false;

            RemoveAllUIEvent();

            for (int i = 0; i < ListChild.Count; i++)
            {
                var uiChild = ListChild[i];
                uiChild.CallDestroy();
                uiChild.OnDestroyWidget();
            }

            // 注销回调函数
            _prepareCallback = null;

            OnDestroy();

            // 销毁面板对象
            if (_panel != null)
            {
                Object.Destroy(_panel);
                _panel = null;
            }
            
            IsDestroyed = true;

            if (!isShutDown)
            {
                CancelHideToCloseTimer();
            }
        }

        /// <summary>
        /// 处理资源加载完成回调。
        /// </summary>
        /// <param name="panel">面板资源实例。</param>
        private void Handle_Completed(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }

            IsLoadDone = true;
            
            if (IsDestroyed)
            {
                Object.Destroy(panel);
                return;
            }
            
            panel.name = GetType().Name;
            _panel = panel;
            _panel.transform.localPosition = Vector3.zero;

            // UICamera 只剔除 UI 层；html-to-ugui 烘焙产物节点层为 Default，会被相机剔除导致整窗不可见
            // （框架原生 prefab 如 LoginUI 逐节点携带 UI 层）。整棵子树迁到 UI 层，原生 prefab 亦幂等。
            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                SetLayerRecursively(panel.transform, uiLayer);
            }

            // 获取组件；html-to-ugui 烘焙产物按约定不带临时 Canvas（apply scope：
            // "Prefab contains no temporary Canvas; wire canvasIntegration onto the host Canvas"），
            // 运行时在窗口根补齐，等价于手工 prefab（LoginUI 等）的根 Canvas + Raycaster。
            _canvas = _panel.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = _panel.AddComponent<Canvas>();
            }

            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 0;
            _canvas.sortingLayerName = "Default";

            // 获取组件
            _raycaster = _panel.GetComponent<GraphicRaycaster>();
            if (_raycaster == null)
            {
                _raycaster = _panel.AddComponent<GraphicRaycaster>();
            }
            _childCanvas = _panel.GetComponentsInChildren<Canvas>(true);
            _childRaycaster = _panel.GetComponentsInChildren<GraphicRaycaster>(true);

            ApplyHtmlBakeFixup();

            // 通知UI管理器
            IsPrepare = true;
            _prepareCallback?.Invoke(this);
        }
        
        /// <summary>
        /// html-to-ugui 设计分辨率（宽/高像素）。非零时 <see cref="Handle_Completed"/> 把窗口根改为
        /// 「中心锚点 + 固定设计尺寸 + 等比缩放适配宿主画布」，替代嵌套 CanvasScaler（嵌套下不生效）。
        /// </summary>
        protected virtual Vector2 DesignResolution => Vector2.zero;

        private static TMP_FontAsset _htmlTmpFont;

        /// <summary>
        /// html-to-ugui 烘焙产物运行时修复：
        /// 1) 烘焙不产出 TMP 字体资产（m_fontAsset=null 的文本完全不渲染），从 Resources/Fonts/zpix.ttf
        ///    惰性创建动态 TMP 字体补挂到缺字体的文本；
        /// 2) 宿主 UICanvas 是 1920×1080 横屏参考（宽高各半匹配）的相机画布，烘焙布局按设计分辨率绝对定位，
        ///    拉伸根会整体溢出；改为居中 + 固定设计尺寸 + 等比缩放（contain，窄屏上下留边）。
        /// </summary>
        private void ApplyHtmlBakeFixup()
        {
            var texts = _panel.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length > 0)
            {
                if (_htmlTmpFont == null)
                {
                    // 工程引入了 TMP 包但从未生成 TMP Settings 资产；CreateFontAsset 内部访问
                    // TMP_Settings.instance 会 NRE。运行时兜底创建内存实例（不写资产文件）。
                    if (TMP_Settings.instance == null)
                    {
                        var settings = ScriptableObject.CreateInstance<TMP_Settings>();
                        typeof(TMP_Settings)
                            .GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic)
                            ?.SetValue(null, settings);
                    }
                    var font = Resources.Load<Font>("Fonts/zpix");
                    if (font != null)
                    {
                        _htmlTmpFont = TMP_FontAsset.CreateFontAsset(font);
                        _htmlTmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        // 编辑器下 TMP 给动态字体加新字形时会尝试把图集纹理 AddObjectToAsset 进这个
                        // 内存资产 → 断言 kDontSaveInEditor（首次点出气泡/新文字时报红）。标记 DontSave 跳过持久化。
                        _htmlTmpFont.hideFlags = HideFlags.DontSave;
                    }
                    else
                    {
                        Log.Error("UIWindow: 缺少 Resources/Fonts/zpix.ttf，烘焙窗口文本无法渲染");
                    }
                }
                if (_htmlTmpFont != null)
                {
                    // 注意 t.font 此时很少为 null：TMP 在 Awake 会把 null 字体替换为 TMP_Settings
                    // 默认字体（LiberationSans SDF，无 CJK→豆腐块）。因此 null 或等于默认字体都要换成 zpix。
                    var defaultFont = TMP_Settings.defaultFontAsset;
                    foreach (var t in texts)
                    {
                        if (t.font == null || t.font == defaultFont)
                        {
                            t.font = _htmlTmpFont;
                        }
                    }
                }
            }

            var design = DesignResolution;
            if (design.x > 0f && design.y > 0f && _panel.transform.parent is RectTransform parentRt)
            {
                _htmlDesignResolution = design;
                _htmlParentRect = parentRt;
                FitHtmlBakeWindow();
            }
        }

        /// <summary>
        /// html 烘焙窗口的 contain 适配：中心锚点 + 固定设计尺寸 + 等比缩放（随宿主画布尺寸）。
        /// 抽成独立方法是因为窗口化拖拽/分辨率切换会改变画布尺寸，需要重算，否则框体脱离屏幕。
        /// </summary>
        private void FitHtmlBakeWindow()
        {
            if (_htmlParentRect == null || _panel == null)
            {
                return;
            }

            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _htmlDesignResolution;
            rt.anchoredPosition = Vector2.zero;
            var parentRect = _htmlParentRect.rect;
            _lastHtmlParentRect = parentRect;
            float scale = Mathf.Min(parentRect.width / _htmlDesignResolution.x, parentRect.height / _htmlDesignResolution.y);
            rt.localScale = new Vector3(scale, scale, 1f);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child, layer);
            }
        }

        protected virtual void Hide()
        {
            UIModule.Instance.HideUI(this.GetType());
        }

        protected virtual void Close()
        {
            UIModule.Instance.CloseUI(this.GetType());
        }
        
        internal void CancelHideToCloseTimer()
        {
            IsHide = false;
            if (HideTimerId > 0)
            {
                ModuleSystem.GetModule<ITimerModule>().RemoveTimer(HideTimerId);
                HideTimerId = 0;
            }
        }
    }
}