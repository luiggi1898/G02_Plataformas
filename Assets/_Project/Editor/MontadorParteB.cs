using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Monta la parte de escenario del juego en la escena abierta:
/// configura los sprites, crea los tiles y los prefabs, y pinta el nivel a partir de Nivel/Nivel_Mapa.txt.
/// Se puede ejecutar varias veces: rehace el Grid y el objeto Nivel.
/// </summary>
public static class MontadorParteB
{
    const string Raiz = "Assets/_Project";
    const string CarpetaTiles = Raiz + "/Sprites/Tiles";
    const string CarpetaObjetos = Raiz + "/Sprites/Objetos";
    const string CarpetaTileAssets = Raiz + "/Tiles";
    const string CarpetaPrefabs = Raiz + "/Prefabs";
    const string CarpetaAnim = Raiz + "/Animations/Objetos";
    const string RutaMapa = Raiz + "/Nivel/Nivel_Mapa.txt";

    const int PixelesPorUnidad = 32;
    const int OrigenX = -13;
    const int OrigenY = -8;
    const float AlturaSuperficie = 0.42f; // La parte transparente de arriba de los tiles de suelo.
    const float YBocaVolcan = 47.5f;
    const float YSuelo = -1.42f;

    static readonly List<string> avisos = new List<string>();

    [MenuItem("GD02/Montar escenario en la escena abierta")]
    public static void Montar()
    {
        avisos.Clear();

        int capaSuelo = LayerMask.NameToLayer("Ground");
        if (capaSuelo < 0)
        {
            EditorUtility.DisplayDialog("Falta la capa Ground",
                "Crea la capa Ground en Edit > Project Settings > Tags and Layers (fase 1 del manual) y vuelve a ejecutar.", "Vale");
            return;
        }

        CrearCarpeta(CarpetaTileAssets);
        CrearCarpeta(CarpetaPrefabs);
        CrearCarpeta(CarpetaAnim);

        ConfigurarSprites(CarpetaTiles);
        ConfigurarSprites(CarpetaObjetos);
        AssetDatabase.Refresh();

        Dictionary<int, TileBase> tiles = CrearTiles();
        Dictionary<string, GameObject> prefabs = CrearPrefabs(capaSuelo);
        PintarNivel(tiles, prefabs, capaSuelo);
        ColocarJugadorYCamara();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        foreach (string aviso in avisos)
        {
            Debug.LogWarning("[GD02] " + aviso);
        }

        string mensaje = avisos.Count == 0
            ? "Escenario montado. Guarda la escena con Ctrl+S y pulsa Play."
            : "Escenario montado con " + avisos.Count + " aviso(s). Míralos en la Console y pásamelos.";
        EditorUtility.DisplayDialog("GD02", mensaje, "Vale");
    }

    // ---------- Sprites ----------

    static void ConfigurarSprites(string carpeta)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { carpeta }))
        {
            string ruta = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importador = AssetImporter.GetAtPath(ruta) as TextureImporter;
            if (importador == null)
            {
                continue;
            }

            importador.textureType = TextureImporterType.Sprite;
            importador.spriteImportMode = SpriteImportMode.Single;
            importador.spritePixelsPerUnit = PixelesPorUnidad;
            importador.filterMode = FilterMode.Point;
            importador.textureCompression = TextureImporterCompression.Uncompressed;
            importador.mipmapEnabled = false;

            TextureImporterSettings ajustes = new TextureImporterSettings();
            importador.ReadTextureSettings(ajustes);
            ajustes.spriteMeshType = SpriteMeshType.FullRect;
            ajustes.spriteGenerateFallbackPhysicsShape = true;
            importador.SetTextureSettings(ajustes);

            importador.SaveAndReimport();
        }
    }

    static Sprite CargarSprite(string carpeta, string nombre)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(carpeta + "/" + nombre + ".png");
        if (sprite == null)
        {
            avisos.Add("No encuentro el sprite " + nombre + " en " + carpeta);
        }
        return sprite;
    }

    // ---------- Tiles ----------

    static Dictionary<int, TileBase> CrearTiles()
    {
        Dictionary<int, TileBase> tiles = new Dictionary<int, TileBase>();
        for (int n = 1; n <= 40; n++)
        {
            Sprite sprite = CargarSprite(CarpetaTiles, "Tile_" + n);
            if (sprite == null)
            {
                continue;
            }

            string ruta = CarpetaTileAssets + "/T_" + n + ".asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(ruta);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, ruta);
            }

            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.Sprite;
            EditorUtility.SetDirty(tile);
            tiles[n] = tile;
        }
        return tiles;
    }

    // ---------- Prefabs ----------

    static Dictionary<string, GameObject> CrearPrefabs(int capaSuelo)
    {
        Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        // Rubí
        GameObject rubi = ObjetoConSprite("Rubi", CargarSprite(CarpetaObjetos, "Rubi_0"), 5);
        CircleCollider2D colRubi = rubi.AddComponent<CircleCollider2D>();
        colRubi.isTrigger = true;
        colRubi.radius = 0.25f;
        AnimarRubi(rubi);
        AnadirScript(rubi, "Coin", new Dictionary<string, object> { { "Value", 10 } });
        prefabs["Rubi"] = GuardarPrefab(rubi, "Rubi");

        // Antorcha (checkpoint)
        GameObject antorcha = ObjetoConSprite("Antorcha", CargarSprite(CarpetaObjetos, "Antorcha_apagada"), 4);
        BoxCollider2D colAntorcha = antorcha.AddComponent<BoxCollider2D>();
        colAntorcha.isTrigger = true;
        colAntorcha.size = new Vector2(1.5f, 3f);
        colAntorcha.offset = new Vector2(0f, 1f);
        GameObject puntoRespawn = new GameObject("RespawnPoint");
        puntoRespawn.transform.SetParent(antorcha.transform, false);
        AnadirScript(antorcha, "Checkpoint", new Dictionary<string, object>
        {
            { "Active Sprite", CargarSprite(CarpetaObjetos, "Antorcha_encendida") },
            { "Inactive Color", Color.white },
            { "Respawn Point", puntoRespawn.transform }
        });
        prefabs["Antorcha"] = GuardarPrefab(antorcha, "Antorcha");

        // Roca flotante (plataforma móvil)
        GameObject roca = ObjetoConSprite("RocaFlotante", CargarSprite(CarpetaTiles, "Tile_13"), 2);
        roca.layer = capaSuelo;
        BoxCollider2D colRoca = roca.AddComponent<BoxCollider2D>();
        colRoca.size = new Vector2(0.96f, 0.58f);
        colRoca.offset = new Vector2(0f, -0.21f);
        Rigidbody2D cuerpoRoca = roca.AddComponent<Rigidbody2D>();
        cuerpoRoca.bodyType = RigidbodyType2D.Kinematic;
        AnadirScript(roca, "MovingPlatform", new Dictionary<string, object>
        {
            { "Move Offset", new Vector2(4f, 0f) },
            { "Speed", 2f }
        });
        prefabs["RocaFlotante"] = GuardarPrefab(roca, "RocaFlotante");

        // Botas de salto
        GameObject botas = ObjetoConSprite("BotasSalto", CargarSprite(CarpetaObjetos, "Botas"), 5);
        CircleCollider2D colBotas = botas.AddComponent<CircleCollider2D>();
        colBotas.isTrigger = true;
        colBotas.radius = 0.3f;
        AnadirScript(botas, "SuperJumpPowerUp", new Dictionary<string, object>
        {
            { "Duration", 6f },
            { "Jump Multiplier", 1.5f }
        });
        prefabs["BotasSalto"] = GuardarPrefab(botas, "BotasSalto");

        // Escudo de hielo
        GameObject escudo = ObjetoConSprite("EscudoHielo", CargarSprite(CarpetaObjetos, "Escudo"), 5);
        CircleCollider2D colEscudo = escudo.AddComponent<CircleCollider2D>();
        colEscudo.isTrigger = true;
        colEscudo.radius = 0.3f;
        AnadirScript(escudo, "ShieldPowerUp", new Dictionary<string, object> { { "Duration", 6f } });
        prefabs["EscudoHielo"] = GuardarPrefab(escudo, "EscudoHielo");

        // Boca del volcán (meta)
        GameObject boca = ObjetoConSprite("BocaVolcan", CargarSprite(CarpetaObjetos, "BocaVolcan"), 6);
        BoxCollider2D colBoca = boca.AddComponent<BoxCollider2D>();
        colBoca.isTrigger = true;
        colBoca.size = new Vector2(22f, 2f);
        colBoca.offset = new Vector2(0f, -0.3f);
        AnadirScript(boca, "Goal", null);
        prefabs["BocaVolcan"] = GuardarPrefab(boca, "BocaVolcan");

        return prefabs;
    }

    static GameObject ObjetoConSprite(string nombre, Sprite sprite, int orden)
    {
        GameObject objeto = new GameObject(nombre);
        SpriteRenderer render = objeto.AddComponent<SpriteRenderer>();
        render.sprite = sprite;
        render.sortingOrder = orden;
        return objeto;
    }

    static void AnimarRubi(GameObject rubi)
    {
        Sprite[] fotogramas = Enumerable.Range(0, 4).Select(i => CargarSprite(CarpetaObjetos, "Rubi_" + i)).ToArray();
        if (fotogramas.Any(s => s == null))
        {
            return;
        }

        const float Fps = 8f;
        ObjectReferenceKeyframe[] claves = new ObjectReferenceKeyframe[fotogramas.Length + 1];
        for (int i = 0; i < claves.Length; i++)
        {
            claves[i].time = i / Fps;
            claves[i].value = fotogramas[i % fotogramas.Length];
        }

        AnimationClip clip = new AnimationClip();
        clip.frameRate = Fps;
        EditorCurveBinding enlace = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, enlace, claves);
        AnimationClipSettings ajustes = AnimationUtility.GetAnimationClipSettings(clip);
        ajustes.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, ajustes);

        string rutaClip = CarpetaAnim + "/Rubi_Brillo.anim";
        string rutaControlador = CarpetaAnim + "/Rubi.controller";
        AssetDatabase.DeleteAsset(rutaClip);
        AssetDatabase.DeleteAsset(rutaControlador);
        AssetDatabase.CreateAsset(clip, rutaClip);
        AnimatorController controlador = AnimatorController.CreateAnimatorControllerAtPathWithClip(rutaControlador, clip);

        rubi.AddComponent<Animator>().runtimeAnimatorController = controlador;
    }

    static GameObject GuardarPrefab(GameObject objeto, string nombre)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(objeto, CarpetaPrefabs + "/" + nombre + ".prefab");
        Object.DestroyImmediate(objeto);
        return prefab;
    }

    // ---------- Scripts del proyecto, localizados por su nombre ----------

    static System.Type BuscarTipo(params string[] nombres)
    {
        foreach (string nombre in nombres)
        {
            System.Type tipo = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == nombre);
            if (tipo != null)
            {
                return tipo;
            }
        }
        return null;
    }

    static void AnadirScript(GameObject objeto, string clase, Dictionary<string, object> campos)
    {
        System.Type tipo = BuscarTipo(clase);
        if (tipo == null)
        {
            avisos.Add("No existe el script " + clase + "; " + objeto.name + " se queda sin él.");
            return;
        }

        Component componente = objeto.GetComponent(tipo);
        if (componente == null)
        {
            componente = objeto.AddComponent(tipo);
        }

        RellenarCampos(componente, campos);
    }

    /// <summary>Rellena campos por la etiqueta que muestran en el Inspector, por ejemplo "Move Offset".</summary>
    static void RellenarCampos(Component componente, Dictionary<string, object> campos)
    {
        if (campos == null)
        {
            return;
        }

        SerializedObject serializado = new SerializedObject(componente);
        foreach (KeyValuePair<string, object> campo in campos)
        {
            SerializedProperty propiedad = BuscarPropiedad(serializado, campo.Key);
            if (propiedad == null || !AsignarValor(propiedad, campo.Value))
            {
                avisos.Add(componente.GetType().Name + ": no he podido rellenar el campo \"" + campo.Key + "\".");
            }
        }
        serializado.ApplyModifiedPropertiesWithoutUndo();
    }

    static SerializedProperty BuscarPropiedad(SerializedObject serializado, string etiqueta)
    {
        SerializedProperty iterador = serializado.GetIterator();
        bool entrar = true;
        while (iterador.NextVisible(entrar))
        {
            entrar = false;
            if (string.Equals(iterador.displayName, etiqueta, System.StringComparison.OrdinalIgnoreCase))
            {
                return iterador.Copy();
            }
        }
        return null;
    }

    static bool AsignarValor(SerializedProperty propiedad, object valor)
    {
        switch (propiedad.propertyType)
        {
            case SerializedPropertyType.Integer:
                propiedad.intValue = System.Convert.ToInt32(valor);
                return true;
            case SerializedPropertyType.Float:
                propiedad.floatValue = System.Convert.ToSingle(valor);
                return true;
            case SerializedPropertyType.Color:
                if (valor is Color)
                {
                    propiedad.colorValue = (Color)valor;
                    return true;
                }
                return false;
            case SerializedPropertyType.Vector2:
                if (valor is Vector2)
                {
                    propiedad.vector2Value = (Vector2)valor;
                    return true;
                }
                return false;
            case SerializedPropertyType.Vector3:
                if (valor is Vector2)
                {
                    propiedad.vector3Value = (Vector2)valor;
                    return true;
                }
                return false;
            case SerializedPropertyType.ObjectReference:
                propiedad.objectReferenceValue = valor as Object;
                return valor is Object;
            default:
                return false;
        }
    }

    // ---------- Nivel ----------

    static void PintarNivel(Dictionary<int, TileBase> tiles, Dictionary<string, GameObject> prefabs, int capaSuelo)
    {
        TextAsset archivo = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaMapa);
        if (archivo == null)
        {
            avisos.Add("No encuentro el mapa en " + RutaMapa);
            return;
        }

        string[] filas = archivo.text.Replace("\r", "").Split('\n').Where(f => f.Length > 0).Reverse().ToArray();

        GameObject anterior = GameObject.Find("Grid");
        if (anterior != null)
        {
            Object.DestroyImmediate(anterior);
        }
        anterior = GameObject.Find("Nivel");
        if (anterior != null)
        {
            Object.DestroyImmediate(anterior);
        }

        GameObject grid = new GameObject("Grid", typeof(Grid));
        Tilemap suelo = CrearTilemap(grid, "Suelo", 0);
        Tilemap plataformas = CrearTilemap(grid, "Plataformas", 0);
        Tilemap decoracion = CrearTilemap(grid, "Decoracion", -5);
        decoracion.transform.localPosition = new Vector3(0f, -AlturaSuperficie, 0f);

        AnadirColisiones(suelo.gameObject, capaSuelo, false);
        AnadirColisiones(plataformas.gameObject, capaSuelo, true);

        Transform nivel = new GameObject("Nivel").transform;
        Transform antorchas = CrearGrupo(nivel, "Antorchas");
        Transform rocas = CrearGrupo(nivel, "RocasFlotantes");
        Transform rubies = CrearGrupo(nivel, "Rubies");
        Transform powerUps = CrearGrupo(nivel, "PowerUps");

        Dictionary<char, int> tilesDecoracion = new Dictionary<char, int> { { 'a', 23 }, { 'c', 21 }, { 'k', 32 } };

        for (int y = 0; y < filas.Length; y++)
        {
            for (int x = 0; x < filas[y].Length; x++)
            {
                char c = filas[y][x];
                Vector3Int celda = new Vector3Int(OrigenX + x, OrigenY + y, 0);
                Vector3 centro = new Vector3(celda.x + 0.5f, celda.y + 0.5f, 0f);

                switch (c)
                {
                    case '#':
                        PonerTile(suelo, celda, tiles, TileSolido(filas, x, y));
                        break;
                    case '=':
                        PonerTile(plataformas, celda, tiles, TilePlataforma(filas, x, y));
                        break;
                    case 'm':
                        Colocar(prefabs, "RocaFlotante", centro, rocas);
                        break;
                    case 'v':
                        GameObject vertical = Colocar(prefabs, "RocaFlotante", centro, rocas);
                        CambiarRecorrido(vertical, new Vector2(0f, 3f));
                        break;
                    case 'r':
                        Colocar(prefabs, "Rubi", centro, rubies);
                        break;
                    case 't':
                        Colocar(prefabs, "Antorcha", centro + Vector3.down * AlturaSuperficie, antorchas);
                        break;
                    case 'b':
                        Colocar(prefabs, "BotasSalto", centro, powerUps);
                        break;
                    case 's':
                        Colocar(prefabs, "EscudoHielo", centro, powerUps);
                        break;
                    default:
                        if (tilesDecoracion.ContainsKey(c))
                        {
                            PonerTile(decoracion, celda, tiles, tilesDecoracion[c]);
                        }
                        break;
                }
            }
        }

        Colocar(prefabs, "BocaVolcan", new Vector3(0f, YBocaVolcan, 0f), nivel);
    }

    static Tilemap CrearTilemap(GameObject grid, string nombre, int orden)
    {
        GameObject objeto = new GameObject(nombre, typeof(Tilemap), typeof(TilemapRenderer));
        objeto.transform.SetParent(grid.transform, false);
        objeto.GetComponent<TilemapRenderer>().sortingOrder = orden;
        return objeto.GetComponent<Tilemap>();
    }

    static void AnadirColisiones(GameObject objeto, int capa, bool atravesable)
    {
        objeto.layer = capa;
        TilemapCollider2D colisionador = objeto.AddComponent<TilemapCollider2D>();
        colisionador.compositeOperation = Collider2D.CompositeOperation.Merge;
        Rigidbody2D cuerpo = objeto.AddComponent<Rigidbody2D>();
        cuerpo.bodyType = RigidbodyType2D.Static;
        CompositeCollider2D compuesto = objeto.AddComponent<CompositeCollider2D>();

        if (atravesable)
        {
            objeto.AddComponent<PlatformEffector2D>();
            compuesto.usedByEffector = true;
        }
    }

    static Transform CrearGrupo(Transform padre, string nombre)
    {
        Transform grupo = new GameObject(nombre).transform;
        grupo.SetParent(padre, false);
        return grupo;
    }

    static void PonerTile(Tilemap mapa, Vector3Int celda, Dictionary<int, TileBase> tiles, int numero)
    {
        TileBase tile;
        if (tiles.TryGetValue(numero, out tile))
        {
            mapa.SetTile(celda, tile);
        }
    }

    static GameObject Colocar(Dictionary<string, GameObject> prefabs, string nombre, Vector3 posicion, Transform padre)
    {
        GameObject copia = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[nombre], padre);
        copia.transform.position = posicion;
        return copia;
    }

    static void CambiarRecorrido(GameObject roca, Vector2 recorrido)
    {
        System.Type tipo = BuscarTipo("MovingPlatform");
        if (tipo == null)
        {
            return;
        }
        RellenarCampos(roca.GetComponent(tipo), new Dictionary<string, object> { { "Move Offset", recorrido } });
    }

    static char Celda(string[] filas, int x, int y)
    {
        if (y < 0 || y >= filas.Length || x < 0 || x >= filas[y].Length)
        {
            return '#';
        }
        return filas[y][x];
    }

    /// <summary>Elige la pieza del bloque 3x3 (tiles 1 a 9) según los vecinos.</summary>
    static int TileSolido(string[] filas, int x, int y)
    {
        bool arriba = Celda(filas, x, y + 1) == '#';
        bool abajo = Celda(filas, x, y - 1) == '#';
        bool izquierda = Celda(filas, x - 1, y) == '#';
        bool derecha = Celda(filas, x + 1, y) == '#';

        int columna = !izquierda ? 0 : (!derecha ? 2 : 1);
        int fila = !arriba ? 0 : (!abajo ? 2 : 1);
        return 1 + fila * 3 + columna;
    }

    /// <summary>Plataforma de una fila: 10 izquierda, 11 centro, 12 derecha, 13 suelta.</summary>
    static int TilePlataforma(string[] filas, int x, int y)
    {
        char izquierda = Celda(filas, x - 1, y);
        char derecha = Celda(filas, x + 1, y);
        bool hayIzquierda = izquierda == '=' || izquierda == '#';
        bool hayDerecha = derecha == '=' || derecha == '#';

        if (hayIzquierda && hayDerecha) return 11;
        if (hayDerecha) return 10;
        if (hayIzquierda) return 12;
        return 13;
    }

    // ---------- Jugador y cámara ----------

    static void ColocarJugadorYCamara()
    {
        GameObject jugador = GameObject.Find("Player");
        if (jugador == null)
        {
            GameObject prefabJugador = AssetDatabase.LoadAssetAtPath<GameObject>(CarpetaPrefabs + "/Player.prefab");
            if (prefabJugador != null)
            {
                jugador = (GameObject)PrefabUtility.InstantiatePrefab(prefabJugador);
            }
        }

        if (jugador == null)
        {
            avisos.Add("No hay Player en la escena ni prefab Player: añádelo para poder jugar.");
        }
        else
        {
            jugador.transform.position = Vector3.zero;
        }

        ConfigurarEnEscena(new[] { "CameraFollowUp" }, new Dictionary<string, object> { { "Max Y", YBocaVolcan - 3f } });
        ConfigurarEnEscena(new[] { "VolcanoSkyBackground", "VolcanoSky" }, new Dictionary<string, object>
        {
            { "Bottom Y", YSuelo },
            { "Top Y", YBocaVolcan }
        });
    }

    static void ConfigurarEnEscena(string[] clases, Dictionary<string, object> campos)
    {
        System.Type tipo = BuscarTipo(clases);
        Object encontrado = tipo == null ? null : Object.FindAnyObjectByType(tipo);
        if (encontrado == null)
        {
            avisos.Add("No hay ningún " + clases[0] + " en la escena; rellena sus campos cuando lo añadas.");
            return;
        }
        RellenarCampos((Component)encontrado, campos);
    }

    static void CrearCarpeta(string ruta)
    {
        Directory.CreateDirectory(ruta);
    }
}
