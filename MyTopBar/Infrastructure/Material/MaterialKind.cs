namespace MyTopBar.Infrastructure.Material
{
    /// <summary>顶栏可选的背景材质。</summary>
    public enum MaterialKind
    {
        /// <summary>不透明纯色，不启用任何系统材质。</summary>
        None = 0,

        /// <summary>Win10 经典毛玻璃（ACCENT_ENABLE_BLURBEHIND）。</summary>
        Blur = 1,

        /// <summary>亚克力（DWMSBT_TRANSIENTWINDOW，失败时回退合成属性）。</summary>
        Acrylic = 2,

        /// <summary>Mica（DWMSBT_MAINWINDOW），Windows 11 22H2+。</summary>
        Mica = 3
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
