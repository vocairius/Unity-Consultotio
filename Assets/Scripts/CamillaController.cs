using UnityEngine;

/// <summary>
/// Controla las 4 partes independientes de la camilla del quirófano.
/// El cliente WebSocket (que recibe el JSON del backend de VetVoz) llama a
/// EjecutarComando() cada vez que llega un comando ya validado por el NLU.
///
/// Supuestos a verificar antes de usar:
/// - 1 unidad de Unity = 1 metro (si tu escena usa otra escala, ajusta las
///   divisiones/multiplicaciones por 1000 en Update()).
/// - "respaldo" es hijo de "asientoPiernas" en la jerarquía, para que el
///   Fowler se sume a la inclinación general del tablero.
/// - "asientoPiernas" es hijo de "columna", para que la altura mueva todo
///   el tablero junto con ella.
/// </summary>
public class CamillaController : MonoBehaviour
{
    [Header("Partes de la camilla (asignar en el Inspector)")]
    [Tooltip("Base fija, no se mueve — referencia del sistema")]
    public Transform baseFija;
    [Tooltip("Columna: sube/baja para la altura")]
    public Transform columna;
    [Tooltip("Sección asiento/piernas: Trendelenburg, tilt lateral, movimiento longitudinal")]
    public Transform asientoPiernas;
    [Tooltip("Sección respaldo: Fowler")]
    public Transform respaldo;

    [Header("Límites físicos")]
    public float alturaMinMM = 700f;
    public float alturaMaxMM = 1100f;
    public float trendelenburgMaxDeg = 30f;
    public float tiltLateralMaxDeg = 20f;
    public float fowlerMaxDeg = 90f;
    public float longitudinalMaxMM = 150f;

    [Header("Suavizado")]
    [Tooltip("Qué tan rápido se acerca cada eje a su objetivo. Más alto = más ágil, más bajo = más lento/suave. 6 es un punto de partida razonable.")]
    public float suavizado = 6f;

    [Header("Incremento por defecto (peticiones naturales sin valor exacto, ej. 'inclínala un poco')")]
    public float incrementoNaturalMM = 20f;
    public float incrementoNaturalDeg = 5f;

    // Estado objetivo (a dónde se dirige cada eje)
    float alturaObjetivoMM;
    float trendelenburgObjetivoDeg;
    float tiltLateralObjetivoDeg;
    float fowlerObjetivoDeg;
    float longitudinalObjetivoMM;

    // Estado actual interpolado (dónde está cada eje ahora mismo, entre 0 y su objetivo)
    float alturaActualMM;
    float trendelenburgActualDeg;
    float tiltLateralActualDeg;
    float fowlerActualDeg;
    float longitudinalActualMM;

    // Estado "de fábrica" de cada pieza, tal como vino del import
    Vector3 columnaPosInicial;
    Vector3 asientoPosInicial;
    Quaternion asientoRotInicial;
    Quaternion respaldoRotInicial;

    void Start()
    {
        columnaPosInicial = columna.localPosition;
        asientoPosInicial = asientoPiernas.localPosition;
        asientoRotInicial = asientoPiernas.localRotation;
        respaldoRotInicial = respaldo.localRotation;
    }

    /// <summary>
    /// Punto de entrada único, pensado para llamarse desde el cliente WebSocket.
    /// tipo: "altura" | "trendelenburg" | "tiltLateral" | "fowler" | "longitudinal"
    /// valor: mm o grados según el tipo (según venga en el JSON del backend).
    /// relativo: true = sumar/restar al valor actual; false = valor absoluto.
    /// esNatural: true si el NLU marcó la petición como vaga ("un poco", "más")
    ///            y no trae un valor exacto — en ese caso se usa el incremento por defecto.
    /// </summary>
    public void EjecutarComando(string tipo, float valor, bool relativo, bool esNatural = false)
    {
        if (esNatural && Mathf.Approximately(valor, 0f))
        {
            bool esAngular = tipo == "trendelenburg" || tipo == "tiltLateral" || tipo == "fowler";
            valor = esAngular ? incrementoNaturalDeg : incrementoNaturalMM;
        }

        switch (tipo)
        {
            case "altura":
                alturaObjetivoMM = Calcular(alturaObjetivoMM, valor, relativo, 0f, alturaMaxMM - alturaMinMM);
                break;
            case "trendelenburg":
                trendelenburgObjetivoDeg = Calcular(trendelenburgObjetivoDeg, valor, relativo, -trendelenburgMaxDeg, trendelenburgMaxDeg);
                break;
            case "tiltLateral":
                tiltLateralObjetivoDeg = Calcular(tiltLateralObjetivoDeg, valor, relativo, -tiltLateralMaxDeg, tiltLateralMaxDeg);
                break;
            case "fowler":
                fowlerObjetivoDeg = Calcular(fowlerObjetivoDeg, valor, relativo, 0f, fowlerMaxDeg);
                break;
            case "longitudinal":
                longitudinalObjetivoMM = Calcular(longitudinalObjetivoMM, valor, relativo, -longitudinalMaxMM, longitudinalMaxMM);
                break;
            default:
                Debug.LogWarning($"Comando no reconocido: {tipo}");
                break;
        }
    }

    float Calcular(float actual, float valor, bool relativo, float min, float max)
    {
        float nuevo = relativo ? actual + valor : valor;
        return Mathf.Clamp(nuevo, min, max);
    }

    void Update()
    {
        // Factor de suavizado independiente del framerate: se acerca cada vez
        // más despacio al objetivo, en vez de moverse a velocidad fija y frenar en seco.
        float t = 1f - Mathf.Exp(-suavizado * Time.deltaTime);

        alturaActualMM = Mathf.Lerp(alturaActualMM, alturaObjetivoMM, t);
        trendelenburgActualDeg = Mathf.Lerp(trendelenburgActualDeg, trendelenburgObjetivoDeg, t);
        tiltLateralActualDeg = Mathf.Lerp(tiltLateralActualDeg, tiltLateralObjetivoDeg, t);
        fowlerActualDeg = Mathf.Lerp(fowlerActualDeg, fowlerObjetivoDeg, t);
        longitudinalActualMM = Mathf.Lerp(longitudinalActualMM, longitudinalObjetivoMM, t);

        // Altura: traslación de la columna en Y (local), relativa a su posición inicial
        columna.localPosition = columnaPosInicial + Vector3.up * (alturaActualMM / 1000f);

        // Trendelenburg (X) + tilt lateral (Z): rotación del tablero completo, relativa a su orientación inicial
        asientoPiernas.localRotation = asientoRotInicial * Quaternion.Euler(trendelenburgActualDeg, 0f, tiltLateralActualDeg);

        // Fowler: rotación solo del respaldo, relativa a su orientación inicial (ya es hijo del tablero)
        respaldo.localRotation = respaldoRotInicial * Quaternion.Euler(-fowlerActualDeg, 0f, 0f);

        // Movimiento longitudinal: deslizamiento del tablero, relativo a su posición inicial
        asientoPiernas.localPosition = asientoPosInicial + new Vector3(0f, 0f, longitudinalActualMM / 1000f);
    }

    // ----- Métodos de prueba -----
    // Click derecho sobre el título "Camilla Controller" en el Inspector (durante Play)
    // para ver estos botones en el menú contextual, sin necesidad de voz ni de código.

    [ContextMenu("Probar: Subir altura")]
    void TestSubirAltura() => EjecutarComando("altura", 50f, true);

    [ContextMenu("Probar: Bajar altura")]
    void TestBajarAltura() => EjecutarComando("altura", -50f, true);

    [ContextMenu("Probar: Trendelenburg +")]
    void TestTrendelenburgMas() => EjecutarComando("trendelenburg", 10f, true);

    [ContextMenu("Probar: Trendelenburg -")]
    void TestTrendelenburgMenos() => EjecutarComando("trendelenburg", -10f, true);

    [ContextMenu("Probar: Tilt lateral +")]
    void TestTiltMas() => EjecutarComando("tiltLateral", 10f, true);

    [ContextMenu("Probar: Tilt lateral -")]
    void TestTiltMenos() => EjecutarComando("tiltLateral", -10f, true);

    [ContextMenu("Probar: Fowler +")]
    void TestFowlerMas() => EjecutarComando("fowler", 15f, true);

    [ContextMenu("Probar: Fowler -")]
    void TestFowlerMenos() => EjecutarComando("fowler", -15f, true);

    [ContextMenu("Probar: Longitudinal +")]
    void TestLongMas() => EjecutarComando("longitudinal", 30f, true);

    [ContextMenu("Probar: Longitudinal -")]
    void TestLongMenos() => EjecutarComando("longitudinal", -30f, true);

    [ContextMenu("Probar: Volver a posición inicial")]
    void TestReset()
    {
        alturaObjetivoMM = 0f;
        trendelenburgObjetivoDeg = 0f;
        tiltLateralObjetivoDeg = 0f;
        fowlerObjetivoDeg = 0f;
        longitudinalObjetivoMM = 0f;
    }
}