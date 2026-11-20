using UnityEngine;

namespace HaruKitEvi.PipelineSteps
{
    /// <summary>給管線步驟修改的測試資產：只有一個數字，方便觀察交易的寫入與回復。</summary>
    [CreateAssetMenu(menuName = "HaruKitEvi/Pipeline/Counter", fileName = "PipelineCounter")]
    public sealed class PipelineCounter : ScriptableObject
    {
        public int value;
    }
}
