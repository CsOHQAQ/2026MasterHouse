using System;
using System.Collections.Generic;

namespace MasterHouse
{
    /// <summary>
    /// 节点暂存。用 List 结构保证确定性遍历（§11.2 禁止依赖 Dictionary 枚举顺序）。
    /// v1 简化：每种物资统一容量上限；capPerItem &lt; 0 表示无限。
    /// 只能由 Manager 调用修改（§2）。
    /// </summary>
    public class ItemStorage
    {
        public class Slot
        {
            public ItemDef Item;
            public int Count;
        }

        /// <summary>按物资首次入库顺序排列，顺序稳定。</summary>
        public readonly List<Slot> Slots = new List<Slot>();

        public readonly int CapPerItem;

        public ItemStorage(int capPerItem)
        {
            CapPerItem = capPerItem;
        }

        public int Get(ItemDef item)
        {
            var slot = Find(item);
            return slot != null ? slot.Count : 0;
        }

        public int GetFreeSpace(ItemDef item)
        {
            return CapPerItem < 0 ? int.MaxValue : Math.Max(0, CapPerItem - Get(item));
        }

        /// <summary>加入物资，超出上限的部分截断；返回实际加入量。</summary>
        public int Add(ItemDef item, int count)
        {
            if (item == null || count <= 0) return 0;
            int add = Math.Min(count, GetFreeSpace(item));
            if (add <= 0) return 0;
            var slot = Find(item);
            if (slot == null)
            {
                slot = new Slot { Item = item };
                Slots.Add(slot);
            }
            slot.Count += add;
            return add;
        }

        /// <summary>取出物资，不足时仅取剩余量；返回实际取出量。</summary>
        public int Remove(ItemDef item, int count)
        {
            if (item == null || count <= 0) return 0;
            var slot = Find(item);
            if (slot == null) return 0;
            int take = Math.Min(count, slot.Count);
            slot.Count -= take;
            return take;
        }

        /// <summary>是否满足配方的全部输入量（假设列表内物资不重复）。</summary>
        public bool HasAll(List<ItemStack> stacks)
        {
            foreach (var s in stacks)
                if (Get(s.Item) < s.Count)
                    return false;
            return true;
        }

        /// <summary>扣除配方全部输入。调用前须先 HasAll 校验。</summary>
        public void ConsumeAll(List<ItemStack> stacks)
        {
            foreach (var s in stacks)
                Remove(s.Item, s.Count);
        }

        /// <summary>配方产出是否全部放得下（假设列表内物资不重复）。</summary>
        public bool CanAddAll(List<ItemStack> stacks)
        {
            foreach (var s in stacks)
                if (GetFreeSpace(s.Item) < s.Count)
                    return false;
            return true;
        }

        /// <summary>写入配方全部产出。调用前须先 CanAddAll 校验。</summary>
        public void AddAll(List<ItemStack> stacks)
        {
            foreach (var s in stacks)
                Add(s.Item, s.Count);
        }

        private Slot Find(ItemDef item)
        {
            foreach (var slot in Slots)
                if (slot.Item == item)
                    return slot;
            return null;
        }
    }
}