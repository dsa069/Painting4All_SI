using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Gestiona qué lienzo está siendo agarrado por cada mano.
/// Implementa patrón Singleton para asegurar que solo existe una instancia.
/// 
/// Funcionalidad:
/// - Registra qué lienzo está siendo agarrado por la mano izquierda o derecha
/// - Valida que una mano no pueda agarrar dos lienzos simultáneamente
/// - Permite desregistrar agarres cuando se suelta
/// 
/// Uso desde Seleccionar_Lienzo:
/// - Al presionar grip/T2: llamar a IsHandAlreadyGripping(hand) para validar
/// - Si devuelve false: procedemos a enganchar y llamamos a RegisterGrip(hand, this)
/// - Al soltar: llamamos a UnregisterGrip(hand)
/// </summary>
public class CanvasGripManager : MonoBehaviour
{
    public enum ActiveHand { Left, Right }
    
    private static CanvasGripManager instance;
    private Dictionary<ActiveHand, Seleccionar_Lienzo> grippedCanvases;  // Mapeo: Mano → Lienzo
    private Dictionary<Seleccionar_Lienzo, ActiveHand> canvasesByGrip;   // Mapeo: Lienzo → Mano (para validación inversa)
    
    public static CanvasGripManager Instance
    {
        get
        {
            if (instance == null)
            {
                // Buscar si ya existe en la escena
                instance = FindObjectOfType<CanvasGripManager>();
                
                if (instance == null)
                {
                    // Si no existe, crear uno nuevo
                    GameObject managerGO = new GameObject("CanvasGripManager");
                    instance = managerGO.AddComponent<CanvasGripManager>();
                    DontDestroyOnLoad(managerGO);
                    Debug.Log("[CanvasGripManager] ✓ Manager creado automáticamente.");
                }
            }
            return instance;
        }
    }
    
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[CanvasGripManager] ⚠ Ya existe una instancia de CanvasGripManager. Destruyendo duplicada.");
            Destroy(gameObject);
            return;
        }
        
        instance = this;
        grippedCanvases = new Dictionary<ActiveHand, Seleccionar_Lienzo>();
        canvasesByGrip = new Dictionary<Seleccionar_Lienzo, ActiveHand>();
        DontDestroyOnLoad(gameObject);
        
        Debug.Log("[CanvasGripManager] ✓ Inicializado.");
    }

    /// <summary>
    /// Verifica si una mano ya está agarrando un lienzo
    /// </summary>
    public bool IsHandAlreadyGripping(ActiveHand hand)
    {
        return grippedCanvases.ContainsKey(hand) && grippedCanvases[hand] != null;
    }

    /// <summary>
    /// Verifica si un lienzo ya está siendo agarrado por otra mano
    /// </summary>
    public bool IsCanvasAlreadyGripped(Seleccionar_Lienzo canvas)
    {
        return canvasesByGrip.ContainsKey(canvas) && canvasesByGrip[canvas] != null;
    }

    /// <summary>
    /// Obtiene qué mano está agarrando un lienzo específico
    /// Devuelve null si nadie lo está agarrando
    /// </summary>
    public ActiveHand? GetHandGrippingCanvas(Seleccionar_Lienzo canvas)
    {
        if (canvasesByGrip.ContainsKey(canvas))
            return canvasesByGrip[canvas];
        
        return null;
    }

    /// <summary>
    /// Registra que una mano está agarrando un lienzo específico
    /// Solo funciona si:
    /// 1. La mano no está ya agarrando otro lienzo
    /// 2. El lienzo no está siendo agarrado por otra mano
    /// Devuelve true si se registró exitosamente, false si hay conflicto
    /// </summary>
    public bool RegisterGrip(ActiveHand hand, Seleccionar_Lienzo canvas)
    {
        // Validación 1: ¿La mano ya agarra otro lienzo?
        if (IsHandAlreadyGripping(hand))
        {
            Debug.LogWarning($"[CanvasGripManager] ⚠ La mano {hand} ya está agarrando otro lienzo. No se puede agarrar múltiples lienzos simultáneamente.");
            return false;
        }
        
        // Validación 2: ¿El lienzo ya está siendo agarrado por otra mano?
        if (IsCanvasAlreadyGripped(canvas))
        {
            ActiveHand? otherHand = GetHandGrippingCanvas(canvas);
            Debug.LogWarning($"[CanvasGripManager] ⚠ El lienzo '{canvas.gameObject.name}' ya está siendo agarrado por la mano {otherHand}. No se puede agarrar el mismo lienzo con dos manos simultáneamente.");
            return false;
        }
        
        // Ambas validaciones pasaron: registrar
        grippedCanvases[hand] = canvas;
        canvasesByGrip[canvas] = hand;
        Debug.Log($"[CanvasGripManager] ✓ Lienzo '{canvas.gameObject.name}' registrado para mano {hand}");
        return true;
    }

    /// <summary>
    /// Desregistra que una mano está agarrando un lienzo
    /// </summary>
    public void UnregisterGrip(ActiveHand hand)
    {
        if (grippedCanvases.ContainsKey(hand))
        {
            var canvas = grippedCanvases[hand];
            grippedCanvases.Remove(hand);
            
            // También remover del mapeo inverso
            if (canvas != null && canvasesByGrip.ContainsKey(canvas))
            {
                canvasesByGrip.Remove(canvas);
            }
            
            Debug.Log($"[CanvasGripManager] ✓ Lienzo desregistrado para mano {hand}: {(canvas != null ? canvas.gameObject.name : "null")}");
        }
    }

    /// <summary>
    /// Obtiene qué lienzo está siendo agarrado por una mano
    /// Devuelve null si la mano no está agarrando nada
    /// </summary>
    public Seleccionar_Lienzo GetGrippedCanvas(ActiveHand hand)
    {
        if (grippedCanvases.ContainsKey(hand))
            return grippedCanvases[hand];
        
        return null;
    }

    /// <summary>
    /// Obtiene la mano opuesta
    /// </summary>
    public ActiveHand GetOppositeHand(ActiveHand hand)
    {
        return hand == ActiveHand.Left ? ActiveHand.Right : ActiveHand.Left;
    }

    /// <summary>
    /// Bloquea la pintura si la mano opuesta está sujetando un lienzo
    /// </summary>
    public bool IsPaintBlockedByGrip(ActiveHand paintHand)
    {
        return IsHandAlreadyGripping(GetOppositeHand(paintHand));
    }

    #if UNITY_EDITOR
    
    /// <summary>
    /// Debug visualization
    /// </summary>
    public void PrintDebugInfo()
    {
        Debug.Log("=== CanvasGripManager State ===");
        Debug.Log($"Mano Izquierda: {(IsHandAlreadyGripping(ActiveHand.Left) ? GetGrippedCanvas(ActiveHand.Left).gameObject.name : "Sin agarrar")}");
        Debug.Log($"Mano Derecha: {(IsHandAlreadyGripping(ActiveHand.Right) ? GetGrippedCanvas(ActiveHand.Right).gameObject.name : "Sin agarrar")}");
    }
    
    #endif
}
