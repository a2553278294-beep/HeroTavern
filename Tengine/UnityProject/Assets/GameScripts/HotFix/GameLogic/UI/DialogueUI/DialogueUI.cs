using GameLogic.World;
using TMPro;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 对话窗口（对照 Resources/ui01/talk/ui对话.png；对话案 3.3）：
    /// 全屏压暗 + 左下立绘占位 + 右下正文长框（名称页签压顶边）。
    /// 点击正文框推进一句；末句播完后再点关闭。期间世界停走——暂停由调用方
    /// （<see cref="WorldHud"/>）在打开前置位，窗口销毁时回调 <c>OnDialogueClosed</c> 恢复。
    ///
    /// 立绘美术未接入：占位为羊皮纸槽 + lv 角花 + NPC 名（同 OnGUI 版占位口径）。
    /// </summary>
    [Window(UILayer.UI)]
    public sealed class DialogueUI : UIWindow
    {
        /// <summary>当前打开的实例（WorldHud 判定「暂停类面板开着」用）。</summary>
        public static DialogueUI Current { get; private set; }

        /// <summary>待播会话（ShowUIAsync 为异步，先寄存、OnCreate 自取）。</summary>
        public static DialogueSession PendingSession;
        /// <summary>待播会话的 NPC。</summary>
        public static CharacterData PendingNpc;

        // ==================== 绑定节点 ====================

        private TextMeshProUGUI _textTitle;
        private TextMeshProUGUI _textPortraitName;
        private TextMeshProUGUI _textBody;
        private TextMeshProUGUI _textProgress;
        private TextMeshProUGUI _textHint;
        private Button _btnAdvance;

        // ==================== 运行时状态 ====================

        private DialogueSession _session;
        private CharacterData _npc;
        private DialogueLine _current;

        #region 脚本工具生成的代码（手工按 ScriptGenerator 约定绑定）

        protected override void ScriptGenerator()
        {
            _textTitle = FindChildComponent<TextMeshProUGUI>("m_goTitleTab/m_tmpTitle");
            _textPortraitName = FindChildComponent<TextMeshProUGUI>("m_goPortrait/m_tmpPortraitName");
            _textBody = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpBody");
            _textProgress = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpProgress");
            _textHint = FindChildComponent<TextMeshProUGUI>("m_goPanel/m_tmpHint");
            _btnAdvance = FindChildComponent<Button>("m_btnAdvance");
            if (_btnAdvance != null)
            {
                _btnAdvance.onClick.AddListener(OnClickAdvance);
            }
        }

        #endregion

        /// <summary>html-to-ugui 设计分辨率：UIWindow 据此对窗口根做居中等比适配（宿主画布为 1920×1080 横屏参考，宽高各半匹配）。</summary>
        protected override Vector2 DesignResolution => new Vector2(1960f, 1080f);

        protected override void OnCreate()
        {
            Current = this;

            // 取寄存的会话；没有则显示占位（点一下即关）。
            if (PendingSession != null)
            {
                var session = PendingSession;
                var npc = PendingNpc;
                PendingSession = null;
                PendingNpc = null;
                Open(session, npc);
            }
            else
            {
                UpdateView();
            }
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            // 对话是暂停类面板：窗口销毁即恢复走时（含重开对局等强制关窗路径）。
            WorldSceneBootstrap.Instance?.Hud?.OnDialogueClosed();
        }

        // ==================== 播放 ====================

        /// <summary>打开并立刻取第一句（会话为 null 或零行时也打开，显示占位，点一下即关）。</summary>
        public void Open(DialogueSession session, CharacterData npc)
        {
            _session = session;
            _npc = npc;
            ShowNextLine();
        }

        private void OnClickAdvance()
        {
            if (_session == null || _session.IsFinished)
            {
                GameModule.UI.CloseUI<DialogueUI>();
                return;
            }

            ShowNextLine();
        }

        private void ShowNextLine()
        {
            _current = _session != null ? _session.Next() : null;
            UpdateView();
        }

        private void UpdateView()
        {
            string npcName = _npc != null ? _npc.Name : "客人";

            if (_textPortraitName != null)
            {
                _textPortraitName.text = npcName;
            }

            if (_textTitle != null)
            {
                // 页签 = 当前句说话者显示名（无当前句时落回 NPC 名）。
                _textTitle.text = _current != null ? ResolveSpeakerName(_current.Speaker, npcName) : npcName;
            }

            if (_textBody != null)
            {
                if (_current == null)
                {
                    _textBody.text = _session == null ? "（没有对话）" : "（对话已经结束，点一下关闭）";
                }
                else
                {
                    _textBody.text = string.IsNullOrEmpty(_current.Text) ? "（这句没有正文）" : _current.Text;
                }
            }

            if (_textProgress != null)
            {
                int total = _session != null ? _session.Lines.Count : 0;
                _textProgress.text = total <= 0
                    ? "（本次没有可播放的台词）"
                    : $"第 {Mathf.Clamp(_session.Cursor, 0, total)} / {total} 句";
            }

            if (_textHint != null)
            {
                _textHint.text = _session != null && !_session.IsFinished ? "▼ 点击继续" : "▼ 点击关闭";
            }
        }

        /// <summary>
        /// 说话者词（配置表中文）→ 显示名。「事件角色 / 客人」映射成当前 NPC 名；未知词原样透出。
        /// </summary>
        private static string ResolveSpeakerName(string speaker, string npcName)
        {
            if (string.IsNullOrEmpty(speaker))
            {
                return npcName;
            }

            if (speaker == DialogueSystem.SpeakerEventNpc || speaker == DialogueSystem.SpeakerGuest)
            {
                return npcName;
            }

            if (speaker == DialogueSystem.SpeakerPlayer)
            {
                return "玩家";
            }

            if (speaker == DialogueSystem.SpeakerSystem)
            {
                return "系统";
            }

            if (speaker == DialogueSystem.SpeakerBartender)
            {
                return "酒保";
            }

            return speaker;
        }
    }
}
