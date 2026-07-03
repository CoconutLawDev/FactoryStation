// FactoryStation-Edit: Сообщение для установки вечного рецепта
using Robust.Shared.Serialization;

namespace Content.Shared.Lathe;

[Serializable, NetSerializable]
public sealed class LatheSetEternalRecipeMessage : BoundUserInterfaceMessage
{
    public bool Eternal;

    public LatheSetEternalRecipeMessage(bool eternal)
    {
        Eternal = eternal;
    }
}
