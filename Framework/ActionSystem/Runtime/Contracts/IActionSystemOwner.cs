namespace HaruFamily.Framework.ActionSystem
{
public interface IActionSystemOwner
{
    void MarkActionSystemDirty();
    bool IsActionSystemValidated();
#if UNITY_EDITOR
    void VerifyActionSystem();
#endif
}

}
