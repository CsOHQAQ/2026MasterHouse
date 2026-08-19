using UnityEngine;
using UnityEngine.UI;

namespace MasterHouse
{
    /// <summary>
    /// 图鉴详情页（2026-08-19 按 2.0 设计图新建）：摊开的书页，左页是档案文字、右页是立绘。
    /// 文字内容全部来自 <see cref="VisitorRaceDef"/>（策划在访客种族表里填，§16.6），
    /// 这里只存槽位与按种族烘好的图；位置尺寸以 Prefab 为准（§16.2）。
    /// </summary>
    public sealed class OutGameCodexDetailView : MonoBehaviour
    {
        [Header("底图与标题")]
        public RawImage background;
        public Text title;

        [Header("左页 · 身份")]
        [Tooltip("名字（大字）")] public Text nameLabel;
        [Tooltip("名字后面的西文别名")] public Text aliasLabel;
        [Tooltip("称号牌（星级 + 称号 一整张图）")] public Image ratingPlate;
        [Tooltip("称号文字，压在称号牌右半边")] public Text titleLabel;
        [Tooltip("三颗星，按星级显隐")] public Image[] stars = new Image[3];

        [Header("左页 · 正文")]
        public Text hobbiesLabel;
        public Text introLabel;

        [Header("左页 · 语录与徽记")]
        [Tooltip("QUOTE 纸底图（素材有四版，按条目下标轮换）")] public Image quotePaper;
        public Sprite[] quotePapers = new Sprite[4];
        public Text quoteLabel;
        [Tooltip("眼睛/月亮那块徽记框")] public Image emblemBox;

        [Header("证件卡")]
        [Tooltip("别在书页上的 POLICE 证件卡底图")] public Image idCard;
        [Tooltip("证件卡上的头像")] public RawImage idAvatar;
        [Tooltip("证件卡上的西文名")] public Text idName;

        [Header("右页")]
        [Tooltip("右页整幅立绘（每族一张）")] public RawImage portrait;
        [Tooltip("右下角帆船装饰")] public Image shipDecor;
        [Tooltip("未解锁提示：没接待过时立绘位置写「未解锁」")] public Text lockedHint;

        [Header("条目（与图鉴页同一份种族顺序）")]
        public VisitorRaceDef[] races;
        public Texture2D[] portraits;
        public Texture2D[] avatars;

        [Header("键位条")]
        public Button backButton;
        [Tooltip("中键：切换角色")] public Button switchButton;
    }
}
