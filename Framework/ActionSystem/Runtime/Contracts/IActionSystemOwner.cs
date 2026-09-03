namespace HaruFamily.Framework.ActionSystem
{
public interface IActionSystemOwner
{
    void MarkActionSystemDirty();
    bool IsActionSystemValidated();
#if UNITY_EDITOR
    void VerifyActionSystem();

    /// <summary>
    /// 本載體實際會被觸發的時機。編輯器的時機選單只列這些值，接不到不會跑的時機。
    /// null＝不限制（列出 TTiming 的全部成員）。型別是中性 Enum：泛型層不認識專案的時機列舉。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<System.Enum> AllowedTimings { get; }
#endif
}

}
