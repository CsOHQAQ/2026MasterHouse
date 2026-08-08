namespace MasterHouse
{
    /// <summary>
    /// 调试选项寄存处（一次性 debug 工具状态，非框架代码；调试面板写、Controller 读）。
    /// </summary>
    public static class DebugOptions
    {
        /// <summary>
        /// 自由模式（需求记录·调试面板权限模型）：绕过 Controller 层资格校验
        /// （可建列表/数量上限/预置 CanMove、CanDelete）；
        /// 放置与走线合法性仍走 CanPlaceNode / A*，不产生脏状态。
        /// </summary>
        public static bool FreeMode;
    }
}
