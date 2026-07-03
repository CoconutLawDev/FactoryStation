using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Automation
{
    [RegisterComponent]
    public sealed partial class AutoCollectorComponent : Component
    {
        /// <summary>
        /// Интервал проверки в секундах.
        /// </summary>
        [DataField("interval")]
        public float Interval = 0.5f;

        /// <summary>
        /// Сила подтягивания предмета к станку.
        /// </summary>
        [DataField("pullForce")]
        public float PullForce = 25f;

        /// <summary>
        /// Радиус сбора предметов. По умолчанию 0.7 (область одного тайла).
        /// </summary>
        [DataField("collectionRadius")]
        public float CollectionRadius = 0.7f;
    }
}
