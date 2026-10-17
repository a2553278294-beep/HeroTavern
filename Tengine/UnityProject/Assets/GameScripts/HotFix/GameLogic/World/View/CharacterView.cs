using GameLogic.World;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// NPC 场景视图：分层贴图角色（body/face/hair，组装与染色规范见 <see cref="SpriteCharacterBuilder"/>），
    /// 形象由 <see cref="CharacterData.Id"/> 确定性派生（发型 01/03、身体配色 01/02）；
    /// 站立播 idle 2 帧、移动播 walk 4 帧；方向规则（策划口径：a=前 b=右 c=左 d=后）：
    /// 移动中 |dy|>|dx| 取前/后、|dx|>|dy| 取右/左；停下后按本次移动的 x 分量收为右(b)/左(c)侧身，纯纵向移动保持前/后。
    /// 位置由 <see cref="WorldSceneBootstrap.CellToWorld"/> 映射到底图平面（pivot=脚底），z 由纵深排序决定。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        /// <summary>视觉移动速度（格/真实秒）。3.0 ≈ 一格 0.33 秒，快走不拖沓。</summary>
        private const float MoveSpeed = 3.0f;

        /// <summary>
        /// 错峰起步上限（真实秒）：逻辑层整点全员同步走 1 格（<c>WorldSimulation.MoveCharactersOneStep</c>），
        /// 视图按 <see cref="CharacterData.Id"/> 确定性延迟 0~MaxStaggerDelay 再起步，避免所有人同帧滑动。
        /// 取值约等于走完一格的时间（1/3 秒）：错峰仍看得出来，但全员走完不会拖出明显等待。
        /// </summary>
        private const float MaxStaggerDelay = 0.35f;

        /// <summary>动画帧间隔（真实秒）：idle 慢速呼吸，walk 快步。</summary>
        private const float IdleFrameInterval = 0.7f;
        private const float WalkFrameInterval = 0.12f;

        /// <summary>各姿态帧数（对应 character_c_{pose}_* 切图：idle/sit 2 帧、walk 4 帧）。</summary>
        private const int IdleFrames = 2;
        private const int WalkFrames = 4;

        public CharacterData Data { get; private set; }

        private CharacterPartLayer[] _layers;
        private bool _walking;
        private int _frame = 1;
        private float _animTimer;

        /// <summary>朝向（策划口径：a=前 b=右 c=左 d=后）。默认前(a)。</summary>
        private string _dir = "a";

        /// <summary>本次移动的累计位移：x 分量用于停下后决定左/右侧身。</summary>
        private Vector2 _moveAccum;

        /// <summary>视图已看到的逻辑格：与 Data.Position 不一致说明整点走了新格，触发错峰延迟。</summary>
        private GridPos _seenCell;

        /// <summary>剩余错峰起步延迟（真实秒）；&gt;0 时原地保持 idle。</summary>
        private float _moveDelay;

        public static CharacterView Create(CharacterData data)
        {
            // 形象由 Id 决定：同一 NPC 跨对局/重开保持同一外观。
            var go = SpriteCharacterBuilder.Build($"NPC_{data.Id}_{data.Name}", data.Id);
            var view = go.AddComponent<CharacterView>();
            view.Data = data;
            view._layers = go.GetComponentsInChildren<CharacterPartLayer>();
            view._seenCell = data.Position; // 出生即同步，不错峰
            go.transform.position = WorldSceneBootstrap.CellToWorld(data.Position);
            return view;
        }

        /// <summary>
        /// 职业着色（原型辨识用）。贴图形象自带配色后暂停使用；
        /// 保留给后续「职业粒子特效标注」（美术说明）做取色基准。
        /// </summary>
        public static Color ProfessionColor(ProfessionType profession)
        {
            switch (profession)
            {
                case ProfessionType.Chef: return new Color(1f, 0.6f, 0.2f);
                case ProfessionType.Merchant: return new Color(1f, 0.9f, 0.3f);
                case ProfessionType.Blacksmith: return new Color(0.6f, 0.6f, 0.65f);
                case ProfessionType.Farmer: return new Color(0.4f, 0.8f, 0.3f);
                case ProfessionType.Warrior: return new Color(0.85f, 0.25f, 0.25f);
                case ProfessionType.Priest: return new Color(0.95f, 0.95f, 0.85f);
                case ProfessionType.Bard: return new Color(0.6f, 0.4f, 0.9f);
                default: return new Color(0.7f, 0.55f, 0.4f);
            }
        }

        private void Update()
        {
            if (Data == null || !Data.IsAlive)
            {
                return;
            }

            // 整点逻辑同步走格 → 视图按 Id 错峰延迟起步，避免全员同帧滑动（延迟期间保持 idle）。
            if (Data.Position != _seenCell)
            {
                _seenCell = Data.Position;
                _moveDelay = (Data.Id * 0.637f) % MaxStaggerDelay; // 确定性伪随机，同 seed 可复现
            }

            if (_moveDelay > 0f)
            {
                _moveDelay -= Time.deltaTime;
                UpdateAnimation(false);
                return;
            }

            // pivot=脚底：直接滑向落点，无需半高偏移。
            var before = transform.position;
            var target = WorldSceneBootstrap.CellToWorld(Data.Position);
            transform.position = Vector3.MoveTowards(before, target, MoveSpeed * Time.deltaTime);

            var delta = transform.position - before;
            bool moving = delta.sqrMagnitude > 2.5e-7f; // ≈0.0005 单位
            if (moving)
            {
                if (!_walking)
                {
                    _moveAccum = Vector2.zero; // 新一轮移动开始
                }

                _moveAccum += new Vector2(delta.x, delta.y);
                _dir = DirectionFromDelta(delta);
            }
            else if (_walking)
            {
                // 移动结束：按本次移动的 x 分量收为右(b)/左(c)侧身；x≈0 的纯纵向移动保持前(a)/后(d)。
                if (Mathf.Abs(_moveAccum.x) > 1e-4f)
                {
                    _dir = _moveAccum.x > 0f ? "b" : "c";
                }
            }

            UpdateAnimation(moving);
        }

        /// <summary>
        /// 世界位移 → 方向字母（a=前 b=右 c=左 d=后）：|dy|>|dx| 时世界 +y=远离镜头 → 后(d)、-y=靠近 → 前(a)；
        /// |dx|>|dy| 时 +x=画面右 → 右(b)、-x=画面左 → 左(c)。
        /// </summary>
        private static string DirectionFromDelta(Vector3 delta)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            {
                return delta.x > 0f ? "b" : "c";
            }

            return delta.y > 0f ? "d" : "a";
        }

        private void UpdateAnimation(bool moving)
        {
            if (moving != _walking)
            {
                _walking = moving;
                _frame = 1;
                _animTimer = 0f;
                ApplyFrame();
                return;
            }

            _animTimer += Time.deltaTime;
            float interval = _walking ? WalkFrameInterval : IdleFrameInterval;
            if (_animTimer < interval)
            {
                return;
            }

            _animTimer -= interval;
            int frameCount = _walking ? WalkFrames : IdleFrames;
            _frame = _frame % frameCount + 1;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            string pose = _walking ? "walk" : "idle";
            foreach (var layer in _layers)
            {
                if (layer != null)
                {
                    layer.SetFrame(pose, _dir, _frame);
                }
            }
        }
    }
}
