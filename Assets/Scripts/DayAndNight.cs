using UnityEngine;

public class DayAndNight : MonoBehaviour
{
    [Header("Materiales del Skybox")]
    public Material simpleSkyMaterial;
    public Material realStarsMaterial;
    public Material sunsetSkyMaterial;
    public Material superNovaSkyMaterial;

    // --- Funciones Públicas (Las que llaman tus botones o eventos) ---

    public void SetForestDay() => ChangeSky(simpleSkyMaterial, "SimpleSky");
    
    public void SetDarkNight() => ChangeSky(realStarsMaterial, "Real Stars");

    public void SetBeachSunset() => ChangeSky(sunsetSkyMaterial, "Atardecer");

    public void SetWhiteSuperNova() => ChangeSky(superNovaSkyMaterial, "Supernova");

    // --- Función Abstraída (La "maestra") ---

    private void ChangeSky(Material newSky, string skyName)
    {
        if (newSky == null)
        {
            Debug.LogWarning($"Intentaste cambiar a {skyName}, pero el material no está asignado en el Inspector.");
            return;
        }

        RenderSettings.skybox = newSky;
        
        // Esto es vital para que la luz de la escena cambie junto con el cielo
        DynamicGI.UpdateEnvironment(); 
        
        Debug.Log($"Cielo cambiado a: {skyName}");
    }
}