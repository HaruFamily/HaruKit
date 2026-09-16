namespace HaruFamily.DependencyCore.GraphKit
{
using System;
using UnityEngine;

// 非泛型 base：Verify 與編輯器不必帶泛型參數就能取節點、結果型別與型別相容判定。
public abstract class FormulaSlotBase
{
    /// <summary>目前接的節點。null＝常數模式，直接用預設值。</summary>
    public abstract GraphNode Node { get; }

    /// <summary>接上或斷開節點。斷開傳 null 即回常數模式。</summary>
    public abstract void SetNode(GraphNode node);

    /// <summary>求值結果型別（TResult）。</summary>
    public abstract Type ResultType { get; }

    /// <summary>公式求值封包型別（TPack）。資產綁定驗證用。</summary>
    public abstract Type PackType { get; }

    /// <summary>
    /// 族身份：具體 Slot 型別本身。同一個結果型別可以有多個族（例：string 同時有 String 與 Key），
    /// 所以「這一格收不收得下那個來源」「Token同不同名」「TokenTable 登記在哪一格」一律看這個，不看結果型別。
    /// </summary>
    // 用 GetType() 而不是另外宣告一個 enum／字串：族本來就是「哪一種 Slot」，多一層宣告就多一處會對不上。
    public Type Kind => GetType();

    /// <summary>不分型別存取預設值，供編輯器輸入框讀寫。</summary>
    public abstract object DefaultObject { get; set; }

    /// <summary>
    /// 常數框要畫成哪個型別。預設＝結果型別；子類可回別的型別，讓畫不出輸入框的結果型別
    /// （清單這種）仍有一格可編的「沒接線時取什麼」。只影響常數框，不影響拉線相容性——
    /// chip、候選過濾、Verify 一律看 <see cref="Kind"/>。
    /// </summary>
    public virtual Type DefaultEditType => ResultType;

    /// <summary>
    /// 這一格是「寫出去」而不是「讀進來」：擁有者執行時把結果交給接上的節點，不向它取值。
    /// </summary>
    // 只影響畫法（接點與線改用輸出色、不畫常數框）。相容判定、求值與驗證一律照舊走
    // Kind／AcceptsBody／Evaluate，不因為方向而分岔——資料上它仍然是一般的「欄位指著節點」。
    public virtual bool IsOutput => false;

    /// <summary>
    /// 這個欄位收不收得下包節點（<see cref="IGraphPack"/>）。預設不收。
    /// </summary>
    // 包不求值，所以結果型別與族對它都沒有意義，只剩「這一格是不是就要收包」這個宣告。
    // 目前唯一會打開它的是把產出寫進包的那種欄位。
    public virtual bool AcceptsPack => false;

    /// <summary>這個欄位能不能接<b>這一顆</b>包。預設只看 <see cref="AcceptsPack"/> 的宣告。</summary>
    // 包的設定會在編輯中改變（例如改成由外部供應內容，就沒有東西可以往裡面寫），
    // 宣告層答不出「這一顆現在還收不收得下」，所以分開問。
    // 覆寫它的欄位要能接受 null：拉線與落點判定會在載體還沒裝好內容時先問一次。
    public virtual bool AcceptsPackObject(GraphNodeContent pack) => AcceptsPack;

    /// <summary>
    /// 從這一格拉到空白處時要直接建好的內容；回 null（預設）＝長一顆空節點，由使用者選。
    /// </summary>
    // 空節點代表「還沒決定要接什麼」，那對一般欄位是有意義的狀態，所以這不是通則也不是依族推導，
    // 而是個別欄位自己宣告：只有「拉出來必定是某一種」的欄位才覆寫它。
    public virtual GraphNodeContent CreateDefaultBody() => null;

    /// <summary>
    /// 從這一格拉到空白處時要直接建好的<b>包</b>；回 null（預設）＝這一格不收包。
    /// </summary>
    // 與 CreateDefaultBody 分開：包不是 body，落地要走 GraphNode.SetPack。
    public virtual GraphNodeContent CreateDefaultPack() => null;

    /// <summary>這個欄位收得下的內嵌公式基底型別（TFormula）。型別選單用它過濾。</summary>
    public abstract Type BodyBaseType { get; }

    /// <summary>這個欄位收得下的公式資產型別（TAsset）。</summary>
    public abstract Type AssetBaseType { get; }

    /// <summary>這個欄位能不能接這個內嵌內容。</summary>
    public abstract bool AcceptsBody(GraphNodeContent body);

    /// <summary>這個欄位能不能接這個資產。</summary>
    public abstract bool AcceptsAsset(ScriptableObject asset);

    /// <summary>這個欄位能不能接這個具名Token。必須是同一族（<see cref="Kind"/>）。</summary>
    public abstract bool AcceptsToken(GraphToken endpoint);
}

}
