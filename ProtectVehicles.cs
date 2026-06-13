namespace Oxide.Plugins;

[Info("Protect Vehicles", "&anhe", "1.0.0")]
[Description("Protects vehicles from other players.")]
public class ProtectVehicles : RustPlugin
{
    #region Boilerplate

    private HashSet<string> generalPurposeVehicles =
        new HashSet<string>
    {
        "hotairballoon",

        "mlrs.entity",
        "magnetcrane.entity",

        "workcart.entity",
        "trainwagona.entity",
        "locomotive.entity",
        "traincaboose.entity"
    };

    #endregion

    #region Helpers

    private BaseEntity GetRootEntity(BaseEntity entity)
    {
        BaseEntity parent;
        while ((parent = entity.parentEntity.Get(true)) != null)
            entity = parent;

        return entity;
    }

    #endregion

    #region Data

    private Dictionary<ulong, Tuple<ulong, ulong>> vehiclesDict = new();

    #endregion

    // Auth

    private object getAuthorisation(BasePlayer player, ulong vehicleId)
    {
        ulong userId = player.userID;
        ulong teamId = player.currentTeam;

        player.ChatMessage($"team: {teamId}");

        if (vehiclesDict.TryGetValue(vehicleId, out var savedData))
        {
            player.ChatMessage($"record exists");

            return (
                // Yours
                savedData.Item1 == userId ||
                // Team
                RelationshipManager.ServerInstance.FindTeam(savedData.Item2)?.members.Contains(userId) == true
            )
                ? null : false;
        }

        player.ChatMessage($"record added");

        vehiclesDict[vehicleId] = Tuple.Create(userId, teamId);
        return null;
    }

    #region Aliases

    private object CanAccess(BasePlayer player, BaseEntity entity)
    {
        var vehicle = GetRootEntity(entity);

        if (generalPurposeVehicles.Contains(vehicle.ShortPrefabName))
            return null;
        
        ulong vehicleId = vehicle.net.ID.Value;

        return getAuthorisation(player, vehicleId);
    }

    private object CanMove(BasePlayer player, BaseEntity entity)
    {
        if (player.GetBuildingPrivilege()?.IsAuthed(player) == true)
            return null;

        ulong vehicleId = GetRootEntity(entity).net.ID.Value;

        return getAuthorisation(player, vehicleId);
    }

    // Mount

    private object CanMountEntity(BasePlayer player, BaseMountable entity) =>
        entity.PrefabName.Contains("vehicle")
            ? CanAccess(player, entity) : null;

    // Loot

    private object CanLootEntity(BasePlayer rider, RidableHorse horse) =>
        CanAccess(rider, horse);

    private object CanLootEntity(BasePlayer player, BaseEntity entity) =>
        entity.PrefabName.StartsWith("assets/content/vehicles/")
            ? CanAccess(player, entity) : null;

    // Move

    private object OnHorseLead(RidableHorse horse, BasePlayer player) =>
        CanMove(player, horse);

    private object OnVehiclePush(BaseVehicle vehicle, BasePlayer player) =>
        CanMove(player, vehicle);
    
    // Damage

    private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) =>
        (
            // Vehicle
            entity.PrefabName.StartsWith("assets/content/vehicles/") &&
            // Damaged by player
            info.InitiatorPlayer is BasePlayer player
        )
            ? CanAccess(player, entity) : null;

    #endregion
}