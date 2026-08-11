namespace MasterHouse
{
    /// <summary>家具表面类型：决定家具允许吸附到哪一类网格。</summary>
    public enum FurnitureSurfaceType
    {
        /// <summary>地面家具（茶几、蒲团等）。</summary>
        Floor,
        /// <summary>桌面家具（只能放在带桌面格的家具上）。</summary>
        Table,
        /// <summary>壁挂家具（挂画、悬挂绿植等）。</summary>
        Wall,
    }
}
