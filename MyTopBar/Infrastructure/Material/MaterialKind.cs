namespace MyTopBar.Infrastructure.Material
{
    /// <summary>
    /// 顶栏背景材质。
    ///
    /// 现在只保留毛玻璃一种，不再可切换，因此没有枚举。
    /// 选择毛玻璃的理由：
    ///   - ACCENT_ENABLE_BLURBEHIND 只做高斯模糊，不加噪点、不叠色调，
    ///     背景纹理仍然可见，观感是"透过磨砂玻璃看"
    ///   - DWM 的 SystemBackdrop（Acrylic / Mica）在窗口长期未激活时会退化，
    ///     而顶栏带 WS_EX_NOACTIVATE 永远不是活动窗口，实测 Acrylic 约 1 分钟后
    ///     变成均匀灰块
    ///   - BLURBEHIND 不受窗口活动状态影响
    ///
    /// 代价：该 API 在 Windows 11 上已被标记为不推荐，未来系统版本可能失效。
    /// 若真失效，可退回 DWM SystemBackdrop 并接受退化，或改用自绘模糊。
    /// </summary>
    public static class MaterialInfo
    {
        /// <summary>当前使用的材质名称，供日志与诊断输出。</summary>
        public const string Current = "Blur (ACCENT_ENABLE_BLURBEHIND)";
    }
}
