namespace PinPlugin.ActionSystem
{
public interface IActionSystemOwner
{
    void MarkActionSystemDirty();
    bool IsActionSystemValidated();
#if UNITY_EDITOR
    void VerifyActionSystem();
    bool IsAutoVerifyOnPlay();
#endif
}

}
