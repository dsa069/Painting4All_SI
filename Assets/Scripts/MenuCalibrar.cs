using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction.Input;

public class MenuCalibrar : MonoBehaviour
{
    public static MenuCalibrar Instance;

    public bool IsCalibrating => estaCalibrando;

    private GameObject canvasObj;
    private TextMeshProUGUI textoInstrucciones;
    private GestureUIController gestureController;

    private bool estaCalibrando = false;
    private int gestosRealizados = 0;
    private const int GESTOS_REQUERIDOS = 3;
    
    private enum Paso { T1, T2, B1, B2, Fin }
    private Paso pasoActual = Paso.T1;

    public float CalibT1, CalibT2, CalibB1, CalibB2;
    private List<float> muestras = new List<float>();

    private void Awake()
    {
        Instance = this;
        gestureController = FindFirstObjectByType<GestureUIController>();
        CrearInterfazDinamica();
    }

    private void CrearInterfazDinamica()
    {
        canvasObj = new GameObject("Calibracion_UI_Auto");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        
        GameObject bg = new GameObject("Fondo");
        bg.transform.SetParent(canvasObj.transform);
        var img = bg.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0, 0, 0, 0.9f);
        var rectBg = bg.GetComponent<RectTransform>();
        rectBg.sizeDelta = new Vector2(450, 300); // Aumentado un poco para que quepan los resultados

        GameObject txtObj = new GameObject("TextoInstrucciones");
        txtObj.transform.SetParent(canvasObj.transform);
        textoInstrucciones = txtObj.AddComponent<TextMeshProUGUI>();
        textoInstrucciones.alignment = TextAlignmentOptions.Center;
        textoInstrucciones.fontSize = 24; // Reducido un pelín para el resumen
        var rectTxt = txtObj.GetComponent<RectTransform>();
        rectTxt.sizeDelta = new Vector2(420, 280);

        canvasObj.transform.localScale = Vector3.one * 0.002f;
        canvasObj.SetActive(false);
    }

    public void IniciarCalibracion()
    {
        if (gestureController == null) gestureController = FindFirstObjectByType<GestureUIController>();

        if (Menu.Instance != null) {
            Canvas c = Menu.Instance.GetComponentInChildren<Canvas>(true);
            if (c != null) c.enabled = false;
        }

        estaCalibrando = true;
        pasoActual = Paso.T1;
        gestosRealizados = 0;
        muestras.Clear();
        
        canvasObj.SetActive(true);
        ActualizarTexto();
    }

    void Update()
    {
        if (!estaCalibrando || gestureController == null) return;

        if (Camera.main != null) {
            canvasObj.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.7f;
            canvasObj.transform.LookAt(Camera.main.transform);
            canvasObj.transform.Rotate(0, 180, 0);
        }

        IHand hand = gestureController.GetRightHand();
        if (hand == null || !hand.IsTrackedDataValid) return;

        float fuerza = 0;
        switch (pasoActual) {
            case Paso.T1: fuerza = hand.GetFingerPinchStrength(HandFinger.Index); break;
            case Paso.T2: fuerza = hand.GetFingerPinchStrength(HandFinger.Middle); break;
            case Paso.B1: fuerza = hand.GetFingerPinchStrength(HandFinger.Ring); break;
            case Paso.B2: fuerza = hand.GetFingerPinchStrength(HandFinger.Pinky); break;
        }

        if (fuerza > 0.85f && !IsInvoking("ResetColorGesto")) {
            RegistrarMuestra(fuerza);
        }
    }

    private void RegistrarMuestra(float f)
    {
        gestosRealizados++;
        muestras.Add(f);
        textoInstrucciones.color = Color.green;
        ActualizarTexto();

        if (gestosRealizados >= GESTOS_REQUERIDOS) {
            GuardarPaso();
        } else {
            Invoke("ResetColorGesto", 0.6f);
        }
    }

    private void ResetColorGesto() => textoInstrucciones.color = Color.white;

    private void GuardarPaso()
    {
        float prom = muestras.Average();
        if (pasoActual == Paso.T1) CalibT1 = prom;
        else if (pasoActual == Paso.T2) CalibT2 = prom;
        else if (pasoActual == Paso.B1) CalibB1 = prom;
        else if (pasoActual == Paso.B2) CalibB2 = prom;

        gestosRealizados = 0;
        muestras.Clear();
        pasoActual++;

        if (pasoActual == Paso.Fin) Finalizar();
        else ActualizarTexto();
    }

    private void ActualizarTexto()
    {
        string d = "";
        switch (pasoActual) {
            case Paso.T1: d = "ÍNDICE"; break;
            case Paso.T2: d = "CORAZÓN"; break;
            case Paso.B1: d = "ANULAR"; break;
            case Paso.B2: d = "MEÑIQUE"; break;
        }
        textoInstrucciones.text = $"<color=yellow>CALIBRANDO:</color> PINZA {d}\nINTENTO {gestosRealizados}/{GESTOS_REQUERIDOS}";
    }

    private void Finalizar()
    {
        string resultados = "<color=green>CALIBRACIÓN COMPLETA</color>\n";
        resultados += $"<size=80%>Índice: {CalibT1:F3}\n";
        resultados += $"Corazón: {CalibT2:F3}\n";
        resultados += $"Anular: {CalibB1:F3}\n";
        resultados += $"Meñique: {CalibB2:F3}</size>";
        
        textoInstrucciones.text = resultados;
        textoInstrucciones.color = Color.white;

        Debug.Log($"RESULTADOS CALIBRACIÓN >> T1:{CalibT1:F4} | T2:{CalibT2:F4} | B1:{CalibB1:F4} | B2:{CalibB2:F4}");

        if (Menu.Instance != null) {
            Canvas c = Menu.Instance.GetComponentInChildren<Canvas>();
            if (c != null) c.enabled = true;
        }

        Invoke("ReactivarGestos", 1.5f); 
        Invoke("CerrarCalibrador", 5.0f);
    }

    private void CerrarCalibrador() => canvasObj.SetActive(false);

    private void ReactivarGestos()
    {
        estaCalibrando = false;
        Debug.Log("Gestos reactivados.");
    }
}