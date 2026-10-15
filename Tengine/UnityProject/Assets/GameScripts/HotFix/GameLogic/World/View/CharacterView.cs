using GameLogic.World;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// NPC 场景视图（原型：胶囊体 + 职业着色，平滑跟随模拟坐标；正式形象待 CharacterPart 美术接入）。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        /// <summary>视觉移动速度（格/真实秒）。</summary>
        private const float MoveSpeed = 1.5f;

        public CharacterData Data { get; private set; }

        public static CharacterView Create(CharacterData data)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"NPC_{data.Id}_{data.Name}";
            go.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
            var view = go.AddComponent<CharacterView>();
            view.Data = data;
            go.transform.position = WorldSceneBootstrap.CellToWorld(data.Position, 0.4f);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = ProfessionColor(data.Profession);
            }

            return view;
        }

        /// <summary>职业着色（原型辨识用，正式版用粒子特效标注——美术说明）。</summary>
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

            var target = WorldSceneBootstrap.CellToWorld(Data.Position, 0.4f);
            transform.position = Vector3.MoveTowards(transform.position, target, MoveSpeed * Time.deltaTime);
        }
    }
}
