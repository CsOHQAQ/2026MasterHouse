using UnityEngine;

namespace MasterHouse
{
    /// <summary>
    /// HouseUI 整页基类：一页 = 一个绑定文件（§16.3）。页面只从 Prefab 实例化并绑定数据/事件——
    /// Prefab 是布局唯一真相源，缺失打 Error 不回退代码布局（§16.2）；PrefabPath 为 null 的页面
    /// 是纯动画/程序页（如开门过场），拿到空根自行填充表现内容。
    /// </summary>
    public abstract class HousePage
    {
        protected HouseUIManager UI { get; private set; }

        public RectTransform Root { get; private set; }

        /// <summary>页面 Prefab 的 Resources 路径；null = 无 Prefab 的纯动画页。</summary>
        protected abstract string PrefabPath { get; }

        internal void Show(HouseUIManager ui)
        {
            UI = ui;
            var prefab = string.IsNullOrEmpty(PrefabPath) ? null : Resources.Load<GameObject>(PrefabPath);
            if (prefab != null)
            {
                var instance = Object.Instantiate(prefab, ui.PageRoot, false);
                instance.name = GetType().Name;
                Root = instance.transform as RectTransform;
                if (Root != null)
                {
                    Root.anchorMin = Vector2.zero;
                    Root.anchorMax = Vector2.one;
                    Root.offsetMin = Vector2.zero;
                    Root.offsetMax = Vector2.zero;
                    Root.localScale = Vector3.one;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(PrefabPath))
                    Debug.LogError("[HouseUI] Prefab 缺失，页面无法呈现（§16.2 不回退代码布局）：" + PrefabPath);
                Root = CreateEmptyRoot(ui.PageRoot, GetType().Name);
            }
            OnEnter();
        }

        internal void Hide()
        {
            OnExit();
            if (Root != null)
            {
                HouseUIUtil.KillTweensUnder(Root);
                Object.Destroy(Root.gameObject);
            }
            Root = null;
        }

        /// <summary>页面进场：Root 已就绪，做数据绑定与入场动效。</summary>
        protected abstract void OnEnter();

        /// <summary>页面退场（Root 销毁前）。</summary>
        protected virtual void OnExit() { }

        /// <summary>ESC 落到页面（叠加层栈为空时）。返回 true 表示已消费。</summary>
        public virtual bool OnEscape() => false;

        /// <summary>每帧输入（仅当前页、且无叠加层时由壳调用）。</summary>
        public virtual void HandleInput() { }

        private static RectTransform CreateEmptyRoot(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
