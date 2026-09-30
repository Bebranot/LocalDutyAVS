using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Popups;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared.ADT.OfferItem;

public abstract partial class SharedOfferItemSystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedHandsSystem _hand = default!;

    private void InitializeInteractions()
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OfferItem, InputCmdHandler.FromDelegate(ToggleOfferMode, handle: false, outsidePrediction: false))
            .Register<SharedOfferItemSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<SharedOfferItemSystem>();
    }

    private void ToggleOfferMode(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return;

        if (!_actionBlocker.CanInteract(uid, null))
            return;

        if (!TryComp<OfferItemComponent>(uid, out var offerItem))
            return;

        var ent = new Entity<OfferItemComponent>(uid, offerItem);

        if (offerItem.IsInOfferMode || offerItem.IsInReceiveMode || offerItem.Target != null)
        {
            Cancel(ent);
            return;
        }

        if (!TryComp<HandsComponent>(uid, out var hands) || hands.ActiveHandId is null)
            return;

        // _Duty: без попапа. OfferItem по умолчанию делит клавишу N с блоком (_Duty Block), и
        // «У вас в руках ничего нет!» вылезало при каждом блоке голыми руками — дезинформация.
        if (_hand.GetActiveItem((uid, hands)) is not { } item)
            return;

        offerItem.IsInOfferMode = true;
        offerItem.Hand = hands.ActiveHandId;
        offerItem.Item = item;

        Dirty(ent);
    }
}
