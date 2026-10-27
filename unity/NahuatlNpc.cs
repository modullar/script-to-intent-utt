// Componente de ejemplo para Unity. Copiar a Assets/Scripts/Phase3/ junto con
// los archivos de src/Intent/. No forma parte de la solución .NET.
//
// Requisitos en el proyecto de Unity:
//   Assets/StreamingAssets/phrases.json          (de data/phrases.json)
//   Assets/StreamingAssets/clips/Audio-NNN-2026.wav (de audio/clean/)
//
// Uso: añadir a Luis (el NPC), asignar un AudioSource y llamar a
// OnTranscript(texto) con lo que devuelva whisper.unity.

using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using NahuatlIntent;

public class NahuatlNpc : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string phrasesFile = "phrases.json";
    [SerializeField] private string clipsFolder = "clips";
    [SerializeField] private bool greetOnStart = true;

    private PhraseTable table;
    private IntentMatcher matcher;

    /// <summary>Última respuesta elegida, por si la UI quiere mostrar subtítulos.</summary>
    public MatchResult LastResult { get; private set; }

    public bool IsReady { get { return matcher != null; } }

    private IEnumerator Start()
    {
        yield return LoadTable();
        if (IsReady && greetOnStart)
        {
            yield return Say(table.FindById("001"));   // "¿Cómo están mis amigos?"
        }
    }

    /// <summary>Lee phrases.json desde StreamingAssets (funciona también dentro del APK en Android).</summary>
    private IEnumerator LoadTable()
    {
        string url = StreamingUrl(phrasesFile);
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("NahuatlNpc: no se pudo leer " + url + ": " + req.error);
                yield break;
            }
            PhraseFile file = JsonUtility.FromJson<PhraseFile>(req.downloadHandler.text);
            table = new PhraseTable(file);
            matcher = new IntentMatcher(table);
            Debug.Log("NahuatlNpc: " + table.Count + " filas cargadas");
        }
    }

    /// <summary>Llamar con el texto en español que devuelve el reconocedor de voz.</summary>
    public void OnTranscript(string spanishText)
    {
        if (!IsReady)
        {
            Debug.LogWarning("NahuatlNpc: la tabla todavía no está cargada");
            return;
        }
        MatchResult result = matcher.Match(spanishText);
        LastResult = result;
        Debug.Log("NahuatlNpc: '" + spanishText + "' -> " + result);
        StartCoroutine(Say(result.Row));
    }

    /// <summary>Carga el WAV del clip y lo reproduce.</summary>
    private IEnumerator Say(PhraseRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.clip)) yield break;
        string url = StreamingUrl(clipsFolder + "/" + row.clip);
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("NahuatlNpc: no se pudo cargar " + url + ": " + req.error);
                yield break;
            }
            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            audioSource.clip = clip;
            audioSource.Play();
            // Subtítulos: row.nahuatl y row.spanish.
        }
    }

    /// <summary>
    /// URL de un archivo de StreamingAssets. En Android streamingAssetsPath ya es
    /// una URL (jar:file://...); en escritorio y en el editor es una ruta normal.
    /// </summary>
    private static string StreamingUrl(string relative)
    {
        string path = Path.Combine(Application.streamingAssetsPath, relative).Replace('\\', '/');
        if (path.Contains("://")) return path;
        return "file://" + path;
    }
}
