using Oxide.Core;

namespace Oxide.Plugins;

[Info("Protect Vehicles", "&anhe", "1.0.1")]
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

    private class VehicleData
    {
        public ulong userId;
        public ulong teamId;
    }

    private Dictionary<ulong, VehicleData> vehiclesDict = new();

    private void Init() =>
        LoadData();

    private void Unload() =>
        SaveData();

    private void OnServerSave() =>
        SaveData();

    private void LoadData()
    {
        try
        {
            vehiclesDict =
                Interface.Oxide.DataFileSystem
                    .ReadObject<Dictionary<ulong, VehicleData>>(Name);

            vehiclesDict ??= new();
        }
        catch
        {
            vehiclesDict = new();
        }
    }

    private void SaveData() =>
        Interface.Oxide.DataFileSystem
            .WriteObject(Name, vehiclesDict);

    #endregion

    // Auth

    private object getAuthorisation(BasePlayer player, ulong vehicleId)
    {
        ulong userId = player.userID;
        ulong teamId = player.currentTeam;

        if (vehiclesDict.TryGetValue(vehicleId, out var savedData))
        {
            bool allowed =
                // Yours
                savedData.userId == userId ||
                // Team
                RelationshipManager.ServerInstance.FindTeam(savedData.teamId)?.members.Contains(userId) == true;

            if (allowed)
            {
                // Refresh owner/team record
                vehiclesDict[vehicleId] = new VehicleData
                {
                    userId = userId,
                    teamId = teamId
                };

                return null;
            }

            return false;
        }

        vehiclesDict[vehicleId] = new VehicleData
        {
            userId = userId,
            teamId = teamId
        };

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