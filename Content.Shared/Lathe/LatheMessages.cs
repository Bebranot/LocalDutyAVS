using Content.Shared.Radio; // _Duty
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Lathe;

[Serializable, NetSerializable]
public sealed class LatheUpdateState : BoundUserInterfaceState
{
    public List<ProtoId<LatheRecipePrototype>> Recipes;

    public LatheRecipeBatch[] Queue;

    public ProtoId<LatheRecipePrototype>? CurrentlyProducing;

    // ADT-Tweak-Start
    public bool HasReagentSlot;

    public bool BeakerInserted;
    // ADT-Tweak-End

    // _Duty-start: прогресс текущей детали и каналы объявлений для нового меню станка
    /// <summary>
    /// Когда началась печать текущей детали; null — станок стоит (нет питания или очередь пуста).
    /// Длительность считает только сервер (смазка, детали), поэтому клиенту передаём готовые значения.
    /// </summary>
    public TimeSpan? ProductionStart;

    public TimeSpan ProductionLength;

    /// <summary>
    /// Каналы, в которые станок сообщает о новых рецептах (LatheAnnouncingComponent — серверный).
    /// </summary>
    public List<ProtoId<RadioChannelPrototype>> AnnounceChannels = new();
    // _Duty-end

    public LatheUpdateState(List<ProtoId<LatheRecipePrototype>> recipes, LatheRecipeBatch[] queue, ProtoId<LatheRecipePrototype>? currentlyProducing = null)
    {
        Recipes = recipes;
        Queue = queue;
        CurrentlyProducing = currentlyProducing;
    }
}

/// <summary>
///     Sent to the server to sync material storage and the recipe queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSyncRequestMessage : BoundUserInterfaceMessage
{

}

/// <summary>
///     Sent to the server when a client queues a new recipe.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheQueueRecipeMessage : BoundUserInterfaceMessage
{
    public readonly string ID;
    public readonly int Quantity;
    public LatheQueueRecipeMessage(string id, int quantity)
    {
        ID = id;
        Quantity = quantity;
    }
}

/// <summary>
///     Sent to the server to remove a batch from the queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheDeleteRequestMessage(int index) : BoundUserInterfaceMessage
{
    public int Index = index;
}

/// <summary>
///     Sent to the server to move the position of a batch in the queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheMoveRequestMessage(int index, int change) : BoundUserInterfaceMessage
{
    public int Index = index;
    public int Change = change;
}

/// <summary>
///     Sent to the server to stop producing the current item.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheAbortFabricationMessage() : BoundUserInterfaceMessage
{
}

[NetSerializable, Serializable]
public enum LatheUiKey
{
    Key,
}
