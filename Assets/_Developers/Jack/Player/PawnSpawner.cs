using System.Collections.Generic;
using PurrNet;
using PurrNet.Logging;
using PurrNet.Modules;
using UnityEngine;

public class PawnSpawner : PurrMonoBehaviour
{
    [SerializeField, HideInInspector] private NetworkIdentity _playerPrefab;
    [SerializeField] private GameObject _pawnPrefab;

    [Tooltip("Even if rules are to not despawn on disconnect, this will ignore that and always spawn a player.")]
    [SerializeField]
    private bool _ignoreNetworkRules;

    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
    private int _currentSpawnPoint;

    // Registry for pawns waiting for their PlayerAgent to be ready
    private static Dictionary<PlayerID, IPawn> _orphanedPawns = new Dictionary<PlayerID, IPawn>();

    // Clear static dictionary when entering play mode to prevent stale references
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticData()
    {
        _orphanedPawns.Clear();
    }

    public static bool TryGetOrphanedPawn(PlayerID playerId, out IPawn pawn)
    {
        if (_orphanedPawns.TryGetValue(playerId, out pawn))
        {
            // Validate the pawn is still valid (not destroyed)
            if (pawn != null && pawn.Identity != null)
            {
                _orphanedPawns.Remove(playerId);
                return true;
            }
            else
            {
                // Remove invalid entry
                _orphanedPawns.Remove(playerId);
                pawn = null;
                return false;
            }
        }
        return false;
    }

    private void Awake()
    {
        CleanupSpawnPoints();
    }

    private void CleanupSpawnPoints()
    {
        bool hadNullEntry = false;
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (!spawnPoints[i])
            {
                hadNullEntry = true;
                spawnPoints.RemoveAt(i);
                i--;
            }
        }

        if (hadNullEntry)
            PurrLogger.LogWarning($"Some spawn points were invalid and have been cleaned up.", this);
    }

    private void OnValidate()
    {
        if (_playerPrefab)
        {
            _pawnPrefab = _playerPrefab.gameObject;
            _playerPrefab = null;
        }
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        if (asServer && manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
        {
            // Always subscribe to the event - scene filtering happens in OnPlayerLoadedScene
            scenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;

            // Try to spawn pawns for players already in the scene (if scene is registered)
            if (manager.TryGetModule(out ScenesModule scenes, true))
            {
                if (scenes.TryGetSceneID(gameObject.scene, out var sceneID) &&
                    scenePlayersModule.TryGetPlayersInScene(sceneID, out var players))
                {
                    foreach (var player in players)
                        OnPlayerLoadedScene(player, sceneID, true);
                }
            }
        }
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (asServer && manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
            scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
    }

    private void OnDestroy()
    {
        if (NetworkManager.main &&
            NetworkManager.main.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
            scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        var main = NetworkManager.main;
        if (!main || !main.TryGetModule(out ScenesModule scenes, true))
            return;

        var unityScene = gameObject.scene;
        if (!scenes.TryGetSceneID(unityScene, out var sceneID))
            return;

        if (sceneID != scene || !asServer)
            return;

        bool isDestroyOnDisconnectEnabled = main.networkRules.ShouldDespawnOnOwnerDisconnect();
        if (!_ignoreNetworkRules && !isDestroyOnDisconnectEnabled &&
            main.TryGetModule(out GlobalOwnershipModule ownership, true) &&
            ownership.PlayerOwnsSomething(player))
            return;

        CleanupSpawnPoints();

        GameObject newPlayer;
        if (spawnPoints.Count > 0)
        {
            var spawnPoint = spawnPoints[_currentSpawnPoint];
            _currentSpawnPoint = (_currentSpawnPoint + 1) % spawnPoints.Count;
            newPlayer = UnityProxy.Instantiate(_pawnPrefab, spawnPoint.position, spawnPoint.rotation, unityScene);
        }
        else
        {
            _pawnPrefab.transform.GetPositionAndRotation(out var position, out var rotation);
            newPlayer = UnityProxy.Instantiate(_pawnPrefab, position, rotation, unityScene);
        }

        var iPawn = newPlayer.GetComponent<IPawn>();

        // Try to possess immediately, otherwise store as orphaned and retry
        if (PlayerAgent.TryGetPlayer(player, out var playerAgent))
        {
            playerAgent.Possess(iPawn);
        }
        else
        {
            _orphanedPawns[player] = iPawn;

            // PlayerAgent may have already spawned and finished OnOwnerChanged before this pawn was created
            // Do a delayed retry to give PlayerAgent time to register itself
            StartCoroutine(RetryPossession(player, iPawn));
        }
    }

    private System.Collections.IEnumerator RetryPossession(PlayerID player, IPawn pawn)
    {
        // Try multiple times over several frames to find the PlayerAgent
        const int maxRetries = 10;
        const float retryDelay = 0.1f; // 100ms between retries

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            yield return new UnityEngine.WaitForSeconds(retryDelay);

            // Check if this pawn is still orphaned (might have been claimed already)
            if (!_orphanedPawns.ContainsKey(player) || _orphanedPawns[player] != pawn)
                yield break;

            // Try to find the PlayerAgent
            if (PlayerAgent.TryGetPlayer(player, out var playerAgent))
            {
                _orphanedPawns.Remove(player);
                playerAgent.Possess(pawn);
                yield break;
            }
        }

        // All retries exhausted - log error for developer debugging
        Debug.LogError($"[PawnSpawner] Failed to find PlayerAgent after {maxRetries} retries. Pawn for player {player} remains orphaned!");
    }
}