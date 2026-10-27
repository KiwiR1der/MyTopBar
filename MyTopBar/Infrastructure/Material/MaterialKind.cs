namespace MyTopBar.Infrastructure.Material
{
    /// <summary>顶栏可选的背景材质。</summary>
    public enum MaterialKind
    {
        /// <summary>不透明纯色，不启用任何系统材质。</summary>
        None = 0,

        /// <summary>
        /// Win10 经典毛玻璃（ACCENT_ENABLE_BLURBEHIND）。
        /// Win11 上 SetWindowCompositionAttribute 只提供 Acrylic，
        /// 效果与 <see cref="Acrylic"/> 相同，因此已从设置项中移除；
        /// 该值仅保留用于兼容旧配置文件。
        /// </summary>
        Blur = 1,

        /// <summary>亚克力（DWMSBT_TRANSIENTWINDOW），随下方内容模糊。</summary>
        Acrylic = 2,

        /// <summary>Mica（DWMSBT_MAINWINDOW），取壁纸配色，Windows 11 22H2+。</summary>
        Mica = 3,

        /// <summary>Mica Alt（DWMSBT_TABBEDWINDOW），对比更突出。</summary>
        MicaAlt = 4
    }

    /// <summary>
    /// 材质应用结果。用于向设置界面如实回报「请求了什么、实际生效了什么」。
    /// </summary>
    public readonly record struct MaterialApplyResult(
        MaterialKind Requested,
        MaterialKind Applied,
        bool UsedFallback,
        string Detail)
    {
        public static MaterialApplyResult Failed(MaterialKind requested, string detail)
            => new(requested, MaterialKind.None, true, detail);
    }
}
