import type { Dictionary } from './i18n';

export const es: Dictionary = {
  common: {
    copy: 'Copiar',
    copied: 'Copiado',
    done: 'Hecho',
    userName: 'Usuario',
    displayName: 'Nombre visible',
    password: 'Contraseña',
    confirmPassword: 'Repite la contraseña',
    cancel: 'Cancelar',
    save: 'Guardar',
    edit: 'Editar',
    delete: 'Eliminar',
    remove: 'Quitar',
    add: 'Añadir',
    back: 'Atrás',
    next: 'Siguiente',
    yes: 'Sí',
    no: 'No',
    actions: 'Acciones',
    close: 'Cerrar',
  },
  mstreamPage: {
    title: 'mStream',
    subtitle: 'Cómo se comporta Arcanist con las apps de mStream.',
    off: 'La extensión de mStream está apagada: las apps no pueden conectarse. Actívala en',
    saved: 'Ajustes de mStream guardados',
    ratings: {
      title: 'Valoraciones',
      description:
        'mStream valora de 1 a 5 estrellas. En el modo StarTrack, decide desde cuántas estrellas de mStream una canción es un StarTrack.',
      threshold: 'Es StarTrack desde',
      from: '{stars} ★ de mStream',
      anyHelp: 'Cualquier valoración la hace StarTrack.',
      thresholdHelp:
        'Desde {stars} estrellas es un StarTrack; menos se guarda como valoración, sin mostrarse en el modo StarTrack.',
      singleStarNow: 'El servidor usa ahora el modo StarTrack.',
      fiveStarsNow:
        'El servidor usa ahora cinco estrellas: las valoraciones de mStream se guardan tal cual y esto no se usa.',
    },
    signIn: {
      title: 'Inicio de sesión',
      description:
        'Las apps de mStream entran con una contraseña de aplicación (Mi cuenta). Puedes dejar que también usen la contraseña de cada usuario.',
      password: 'Permitir entrar con la contraseña del usuario',
      warning:
        'La contraseña del usuario viajará en cada inicio de sesión de la app: usa HTTPS. Una contraseña de aplicación se puede revocar sin cambiar la principal. Cuando Arcanist tenga segundo factor, los usuarios que lo activen seguirán necesitando una contraseña de aplicación.',
    },
    quickConnect: {
      title: 'Conexión rápida',
      description:
        'Las apps de mStream se conectan escaneando un código, a través de la red P2P: sin abrir puertos ni conocer la dirección del servidor. Después inician sesión como siempre.',
      enabled: 'Permitir la conexión rápida',
      needsP2p: 'Necesita la red P2P activa. Actívala en',
      qr: 'Código de conexión rápida',
      scan: 'En la app de mStream, elige conectar con un código y escanéalo (o pega el texto).',
      code: 'Código de conexión',
      rotate: 'Cambiar el código',
      rotateHelp: 'Las apps que usaban el anterior se desconectan y tendrán que escanear el nuevo.',
      rotated: 'Código cambiado',
      starting: 'La red P2P se está iniciando; el código aparecerá en unos segundos.',
    },
    features: {
      title: 'Funciones de las apps',
      description:
        'Funciones propias de las apps de mStream que Arcanist ofrece con lo que ya tiene. Apagadas, el servidor dice que no las tiene y las apps las ocultan.',
      WAVEFORM: {
        name: 'Forma de onda',
        help: 'La forma de cada canción, del análisis de audio de Arcanist.',
      },
      STATS: {
        name: 'Estadísticas de escucha',
        help: 'Las estadísticas de mStream 6.27: historial, lo más escuchado, resúmenes y series por día u hora, sobre las reproducciones de Arcanist.',
      },
      SIMILAR: {
        name: 'Similares y viajes',
        help: 'Canciones y artistas parecidos, y viajes de una canción a otra, según tempo, tonalidad, géneros y artistas relacionados.',
      },
      SCAN_STATUS: {
        name: 'Progreso del escaneo',
        help: 'Los escaneos en curso y el avance del análisis de audio.',
      },
    },
    unavailable: {
      title: 'No disponibles en Arcanist',
      description: 'Funciones de mStream sin equivalente en Arcanist. Las apps no las muestran.',
      badge: 'No disponible',
      federation: {
        name: 'Federación',
        help: 'Conectar servidores mStream entre sí. Arcanist tiene su propia red P2P, que no es compatible.',
      },
      torrents: { name: 'Torrents', help: 'Descargar y compartir música por BitTorrent.' },
      backups: {
        name: 'Copias de seguridad',
        help: 'Copias programadas de las bibliotecas a otras carpetas.',
      },
      discoveryCatalog: {
        name: 'Catálogo de descubrimiento P2P',
        help: 'Compartir huellas de canciones con otros servidores mStream para recomendaciones.',
      },
      youtube: { name: 'Descargas de YouTube', help: 'Bajar audio de YouTube a la biblioteca.' },
      discoveryPlugins: {
        name: 'Complementos de descubrimiento',
        help: 'Servicios externos que buscan y bajan música recomendada.',
      },
    },
  },
  theme: {
    label: 'Tema',
    system: 'Tema del sistema',
    light: 'Claro',
    dark: 'Oscuro',
  },
  nav: {
    menu: 'Menú',
    videos: 'Videoclips',
    transcoding: 'Transcodificación',
    extensionsToggle: 'Mostrar u ocultar las extensiones',
    ai: 'AI',
    backups: 'Copias de seguridad',
    cloud: 'Ultimate Cloud',
    database: 'Base de datos',
    network: 'Red P2P',
    mstream: 'mStream',
    dlna: 'DLNA',
    serverAudio: 'Audio del servidor',
    extensions: 'Extensiones',
    overview: 'Inicio',
    libraries: 'Bibliotecas',
    users: 'Usuarios',
    settings: 'Ajustes',
    duplicates: 'Duplicados',
    import: 'Importación',
    stats: 'Estadísticas',
    account: 'Mi cuenta',
    wishes: 'Lista de deseos',
    player: 'Abrir el reproductor',
    logout: 'Cerrar sesión',
  },
  login: {
    restoredTitle: 'Servidor restaurado',
    restored:
      'El servidor volvió tal como estaba en la copia: inicia sesión con una cuenta de entonces.',
    subtitle: 'Inicia sesión para administrar tu servidor de música.',
    submit: 'Iniciar sesión',
  },
  reauth: {
    title: 'Confirma tu contraseña',
    description: 'Esta acción es sensible. Vuelve a escribir tu contraseña para continuar.',
    confirm: 'Confirmar',
  },
  privacyLevel: {
    label: 'Nivel de privacidad',
    open: {
      title: 'Todo activado',
      help: 'Servicios en línea, Ultimate Cloud y estadísticas anónimas de lo que se escucha.',
    },
    private: {
      title: 'Sin telemetría',
      help: 'No sale ninguna estadística de lo que se escucha. Los servicios en línea y Ultimate Cloud siguen funcionando.',
    },
    closed: {
      title: 'Nada sale del servidor',
      help: 'Sin telemetría, sin servicios en línea y sin Ultimate Cloud.',
    },
    custom: 'Personalizada: una combinación de las opciones de abajo.',
    showDetails: 'Ajustar en detalle',
    hideDetails: 'Ocultar el detalle',
  },
  setup: {
    openSource: {
      title: 'Modo 100% open source',
      help: 'Solo se instalan y funcionan componentes de código abierto: extensiones, reproductores y herramientas cerrados (como Ultimate Web Player) no se pueden usar hasta que lo desactives en Ajustes.',
    },
    edition: 'Configuración',
    ultimate: {
      intro:
        'Arcanist funciona con cualquier app compatible, pero brilla con Ultimate, los clientes oficiales de LinkFast: hablan con tu servidor de forma nativa y llevan tu música, tus StarTracks y tus estadísticas a todos tus dispositivos.',
      desktop: {
        tagline: 'La app de escritorio oficial',
        features:
          'AI DJ: tu agente de IA favorito pincha para ti, con transiciones por tempo y tonalidad · Ultimate Connect: controla la música de cualquier dispositivo desde otro, también la que suena en este servidor · Tu biblioteca, tus playlists y tus StarTracks, sin conexión cuando haga falta · Visualizaciones y reproducción sin cortes',
        free: 'Se conecta a este servidor con tu usuario de Arcanist.',
      },
      cloud: {
        tagline: 'Tu música, también en la nube',
        features:
          'Úsala como una biblioteca más de este servidor, y Cloud Drop para traer canciones desde cualquier sitio · Sincronízala con tus bibliotecas en ambos sentidos · Bibliotecas en S3 y conexión P2P transparente con el plan Cloud · Los rankings globales de Ultimate, alimentados por las estadísticas anónimas',
        plans:
          'Una cuenta gratuita basta para usarla como biblioteca; el plan Cloud desbloquea el resto. Se vincula después, desde el panel.',
      },
      enhancers: 'Mejoras de IA y Ultimate para este servidor',
    },
    restore: {
      offerTitle: '¿Vienes de una copia de seguridad?',
      arcanist: 'Otro servidor Arcanist',
      arcanistHelp:
        'Trae otro Arcanist que sigue funcionando (su base de datos, su música, sus usuarios y sus ajustes) sin necesidad de una copia de seguridad.',
      arcanistCode:
        'Necesitas un código de migración del otro servidor: Importación → «Migrar este servidor a otro».',
      arcanistAccount:
        'Antes eliges la base de datos y creas tu cuenta; después se abre la migración para elegir qué traer.',
      arcanistGo: 'Continuar con la migración',
      migrateNext: 'Crea la base de datos y tu cuenta: luego eliges qué traer del otro Arcanist.',
      offer:
        'Si tienes una copia de un servidor Arcanist con su base de datos, puedes restaurarlo entero en lugar de configurarlo desde cero.',
      open: 'Restaurar desde una copia de seguridad',
      title: 'Restaurar un servidor',
      description:
        'Busca la copia en una carpeta de este equipo o en un bucket S3. El servidor vuelve con sus cuentas, ajustes y bibliotecas.',
      folder: 'Carpeta del destino',
      folderHelp:
        'La carpeta donde se guardaban las copias (la que contiene la carpeta de cada copia).',
      search: 'Buscar copias',
      none: 'Aquí no hay copias con la base de datos del servidor.',
      password: 'Contraseña de la copia',
      passwordHelp: 'La que se eligió al incluir el servidor en la copia.',
      check: 'Comprobar',
      passwordOk: 'La contraseña es correcta.',
      passwordWrong: 'Contraseña incorrecta.',
      files: 'Traer también los archivos que falten en las bibliotecas',
      filesHelp:
        'Después de reiniciar, la música que no esté en las carpetas de las bibliotecas se copia desde la copia de seguridad.',
      supervisor:
        'Al restaurar, el servidor se detiene y vuelve a arrancar solo. Eso funciona con el instalador de Windows, el servicio, systemd o Docker con --restart; si lo arrancaste a mano, tendrás que volver a iniciarlo.',
      start: 'Restaurar',
      restoring: {
        title: 'Restaurando el servidor',
        description: 'Se instala la copia y el servidor se reinicia. No cierres esta página.',
        installing: 'Instalando la copia y reiniciando el servidor…',
        restarting: 'Esperando a que el servidor vuelva a arrancar…',
        lateTitle: 'El servidor no ha vuelto',
        late: 'Si no lo arranca un instalador, servicio o contenedor que lo reinicie, inícialo de nuevo a mano: la restauración termina al arrancar. Esta página seguirá esperando.',
      },
    },
    importContinues: 'La importación sigue en segundo plano: su progreso está en Importación.',
    extrasFailed: 'No se pudo aplicar «{what}»: {error}',
    users: {
      explanation:
        'La cuenta de administrador ya existe. Añade a quien vaya a usar este servidor: cada uno tendrá sus valoraciones, playlists y estadísticas.',
      migratedFrom: 'Se migrará desde {server}',
      userName: 'Nombre de usuario',
      displayName: 'Nombre visible',
      password: 'Contraseña',
      admin: 'Administrador',
      download: 'Puede descargar',
      libraries: 'Bibliotecas restringidas que puede escuchar',
      add: 'Añadir un usuario',
      later: 'Podrás añadir, cambiar o quitar usuarios cuando quieras en Usuarios.',
      invalidName:
        'Cada usuario necesita un nombre de 3 a 64 letras, números, puntos, guiones o guiones bajos.',
      duplicate: 'Hay dos usuarios con el mismo nombre.',
      shortPassword: 'Cada contraseña necesita al menos 8 caracteres.',
    },
    p2p: {
      explanation:
        'La red P2P conecta tus dispositivos y otros servidores Arcanist con este sin abrir puertos en el router, con cifrado de extremo a extremo. Está apagada salvo que la actives.',
      enable: 'Activar la red P2P',
      name: 'Nombre de este servidor en la red',
      namePlaceholder: 'Arcanist de casa',
      relay: {
        N0: {
          name: 'Relays públicos de n0 (recomendado)',
          help: 'Conecta detrás de cualquier router. El tráfico sigue cifrado de extremo a extremo.',
        },
        CUSTOM: { name: 'Mi propio relay', help: 'Un relay de iroh que administras tú.' },
        DIRECT: {
          name: 'Solo conexiones directas',
          help: 'Sin relays: algunas redes no podrán conectar.',
        },
      },
      relayUrl: 'Dirección del relay',
      relayUrlRequired: 'Escribe la dirección de tu relay (https://…).',
      later: 'Podrás cambiarlo y emparejar dispositivos en Red P2P.',
    },
    addons: {
      builtIn: 'Extensiones integradas',
      extensions: {
        dlna: {
          name: 'DLNA',
          help: 'Televisores, altavoces y apps de la red local ven y reproducen tus bibliotecas publicadas.',
        },
        subsonic: {
          name: 'Subsonic',
          help: 'Apps como Symfonium, DSub, Feishin o Amperfy, con contraseñas de aplicación.',
        },
        mstream: {
          name: 'mStream',
          help: 'Las apps de mStream se conectan como a un servidor mStream.',
        },
        ai: {
          name: 'AI DJ',
          help: 'Usa tu agente de IA favorito como tu DJ personal: Arcanist analiza cada canción (beats, secciones, energía, voz y puntos de mezcla) para que Ultimate mezcle como un DJ de verdad.',
        },
        'server-audio': {
          name: 'Audio del servidor',
          help: 'Los altavoces conectados a este equipo suenan desde «reproducir en» y, con Ultimate Connect, desde cualquier app Ultimate.',
        },
      },
      ultimate: {
        title: 'Ultimate, los reproductores oficiales',
        description:
          'Ultimate Desktop y Ultimate Web Player, de LinkFast, son los clientes oficiales de Arcanist: hablan con tu servidor de forma nativa y llevan tu biblioteca, tus StarTracks y tus estadísticas a todos tus dispositivos. Las apps móviles llegarán pronto.',
        cloud:
          'Con Ultimate Cloud, tu música también vive en la nube: úsala como una biblioteca más, sincronízala con este servidor y escúchala aunque tu casa esté desconectada.',
        extensions: 'Extensiones para sacarle todo el partido a Ultimate',
        learnMore: 'Conocer Ultimate y Ultimate Cloud',
      },
      player: 'Reproductor web',
      playerHelp:
        'Instala un reproductor desde la dirección de su .zip (opcional). Si ya viene uno, se usará ese.',
      playerDefault: 'Usarlo como reproductor principal',
      more: 'Otras extensiones',
      addExtension: 'Añadir una extensión por dirección',
      trust:
        'Las extensiones y los reproductores se ejecutan con plena confianza: instala solo lo que conozcas. Las extensiones instaladas funcionan tras reiniciar el servidor.',
    },
    stepOf: 'Paso {current} de {total}',
    jump: 'Ir a un paso',
    draft: {
      passwordAgain:
        'Por seguridad, las contraseñas no se guardan al recargar: escribe de nuevo la de tu cuenta.',
      connectionAgain:
        'Por seguridad, la conexión a PostgreSQL no se guarda al recargar: escríbela de nuevo.',
      usersAgain:
        'Por seguridad, las contraseñas no se guardan al recargar: escribe de nuevo las de los usuarios.',
    },
    finish: 'Terminar la configuración',
    terms: {
      required: 'Tienes que aceptar los términos y el descargo de responsabilidad para continuar.',
    },
    steps: {
      ultimate: {
        title: 'Ultimate',
        description: 'Los reproductores oficiales y la nube de tu música.',
      },
      users: {
        title: 'Usuarios',
        description: 'Quién más escucha en este servidor. Puedes saltarlo.',
      },
      p2p: {
        title: 'Red P2P',
        description: 'Escuchar fuera de casa sin abrir puertos. Puedes saltarlo.',
      },
      addons: {
        title: 'Extensiones y reproductores',
        description: 'Tus apps y tus dispositivos. Puedes saltarlo.',
      },
      code: {
        title: 'Código de configuración',
        description: 'Estás configurando Arcanist desde otro dispositivo.',
      },
      terms: {
        title: 'Términos y condiciones',
        description: 'Léelos y acéptalos para usar Arcanist.',
      },
      welcome: {
        title: 'Te damos la bienvenida a Arcanist',
        description: 'Tu propio servicio de música, en tu casa.',
      },
      database: {
        title: 'Base de datos',
        description: 'Dónde guarda Arcanist su catálogo, usuarios y ajustes.',
      },
      admin: {
        title: 'Cuenta de administrador',
        description: 'La cuenta principal, con acceso a todo.',
      },
      import: {
        title: '¿Vienes de otro servidor?',
        description: 'Arcanist puede traer tu música, tus usuarios y sus datos.',
      },
      libraries: {
        title: 'Tus bibliotecas',
        description:
          'La carpeta donde está tu música. Cada biblioteca tiene una: si tu música está en varias carpetas, crea una biblioteca para cada una.',
      },
      media: { title: 'Audio', description: 'Herramientas para convertir y analizar tu música.' },
      ratings: {
        title: 'Valoraciones',
        description: 'Cómo se califica la música en este servidor.',
      },
      privacy: { title: 'Privacidad', description: 'Tú decides qué se comparte.' },
      review: { title: 'Todo listo', description: 'Revisa la configuración antes de terminar.' },
      progress: {
        title: 'Preparando tu música',
        description: 'Arcanist está leyendo tus bibliotecas.',
      },
    },
    code: {
      label: 'Código',
      help: 'Lo encontrarás en el registro (log) del servidor, o en el archivo setup-code.txt de su carpeta de datos. Desde el propio servidor no hace falta.',
    },
    reconfigure: {
      title: 'El asistente, de nuevo',
      description: 'Cambia lo que elegiste al instalar Arcanist.',
      intro:
        'Repasa el idioma, las bibliotecas y usuarios nuevos, el ffmpeg, las valoraciones, la privacidad, la red P2P y las extensiones. La base de datos, las cuentas y las bibliotecas que ya tienes y su música no cambian.',
      afterwards:
        'Al terminar se guardan estas respuestas y se crean las bibliotecas y los usuarios nuevos.',
    },
    reconfigured: 'Configuración guardada',
    welcome: {
      intro:
        'En unos pasos tendrás Arcanist funcionando: elegiremos la base de datos, crearemos tu cuenta y añadiremos tu música.',
      language: 'Idioma',
    },
    database: {
      recommended: 'Recomendado',
      sqliteHelp:
        'Sin instalar nada más. Se guarda en un archivo dentro de la carpeta de datos de Arcanist.',
      postgresHelp:
        'Para bibliotecas muy grandes o muchos usuarios a la vez. Necesitas un servidor PostgreSQL.',
      whenTitle: '¿Cuándo elegir PostgreSQL?',
      whenHelp:
        'SQLite basta para la mayoría de hogares: hasta unas 150.000–200.000 canciones y una decena de personas escuchando a la vez. Elige PostgreSQL si tienes más de unas 250.000 canciones, muchos usuarios simultáneos, o si la carpeta de datos de Arcanist estaría en una unidad de red (SQLite no debe guardarse en red). Podrás migrar de SQLite a PostgreSQL más adelante.',
      connectionString: 'Cadena de conexión',
      connectionStringRequired: 'Escribe la cadena de conexión de PostgreSQL.',
      test: 'Probar conexión',
      ok: 'Conexión correcta',
      locked: 'Configurada por el entorno',
      lockedHelp:
        'La base de datos ya está definida en la configuración del servidor (por ejemplo, variables de entorno de Docker).',
    },
    admin: {
      passwordHelp: 'Al menos 8 caracteres.',
      userNameTooShort: 'El usuario necesita al menos 3 caracteres.',
      passwordTooShort: 'La contraseña necesita al menos 8 caracteres.',
      passwordsDiffer: 'Las contraseñas no coinciden.',
    },
    import: {
      dlna: 'Un servidor DLNA/UPnP',
      kinds: {
        arcanist: {
          name: 'Otro Arcanist',
          help: 'Sus bibliotecas, usuarios, datos, ajustes y extensiones, por la red o con su enlace P2P.',
        },
        mstream: {
          name: 'mStream',
          help: 'Bibliotecas, usuarios con sus contraseñas, playlists, valoraciones y reproducciones.',
        },
        dlna: {
          name: 'Un servidor DLNA',
          help: 'La música de otro servidor de tu red (MinimServer, Serviio, un NAS…). Solo archivos.',
        },
      },
      chosen: 'Elegido',
      clear: 'No importar',
      use: 'Usar esta importación',
      blocks:
        'La importación empieza al terminar el asistente; este muestra su progreso y no abre el panel hasta que acabe (aunque recargues la página).',
      continuesAgain:
        'La importación empieza al terminar y sigue en segundo plano: verás su progreso en Importación.',
      dlnaSummary: '{server}: {folders}',
      arcanistSummary: '{server}: {libraries} biblioteca(s) y {users} usuario(s)',
      dlnaHelp:
        'Copia la música de otro servidor de tu red (MinimServer, Serviio, un NAS…). Solo archivos: no trae usuarios.',
      dlnaPick: 'Elegir servidor y carpetas',
      dlnaInto: 'En una biblioteca nueva, en {place}',
      dlnaLater:
        'La copia empieza al terminar y sigue en segundo plano: verás su progreso en Importación.',
      dlnaChoose: 'Elige el servidor DLNA y las carpetas que quieres copiar.',
      mstreamAgainHelp: 'Trae sus bibliotecas, usuarios y sus datos a este servidor.',
      mstreamWaits:
        'Es una migración: al terminar, el asistente muestra su progreso y espera a que acabe antes de abrir el panel.',
      none: 'Empezar de cero',
      noneHelp: 'Añadirás las carpetas de tu música en el siguiente paso.',
      mstreamHelp:
        'Bibliotecas, usuarios con sus contraseñas, playlists, valoraciones y reproducciones. mStream no se modifica.',
      analyzeFirst: 'Lee la instalación de mStream antes de continuar.',
      incomplete: 'Pon nombre y carpeta a cada biblioteca que vayas a importar.',
    },
    libraries: {
      current: 'Bibliotecas actuales',
      currentHelp:
        'Se quedan como están; para cambiarlas o quitarlas, ve a Bibliotecas. Aquí puedes añadir otras.',
      add: 'Añadir una biblioteca',
      fromMstream: 'Se importará desde mStream ({vpath})',
      fromDlna: 'Se copiará desde {server} (DLNA)',
      fromArcanist: 'Se migrará desde {library} de {server}',
      namePlaceholder: 'Música, Audiolibros…',
      nameRequired: 'Pon un nombre a cada biblioteca con carpetas.',
      another: 'Otra biblioteca',
      networkHelp:
        'Las carpetas pueden estar en otro disco o en una unidad de red montada en el servidor.',
    },
    media: {
      explanation:
        'Arcanist usa ffmpeg para convertir canciones a formatos más ligeros al escuchar fuera de casa y para analizar algunos formatos (como Opus o WMA). Es opcional: si no lo tienes, puedes descargarlo aquí ahora o más tarde desde Ajustes.',
      installing: 'Instalando ffmpeg…',
      downloadHelp:
        'Una build estática de BtbN/FFmpeg-Builds (GPL) comprobada con su SHA-256, en la carpeta de datos de Arcanist.',
      found: 'Encontrado ffmpeg {version} en {path}.',
      notFound: 'No se ha encontrado ffmpeg en este servidor.',
      path: 'Usar otro ffmpeg (opcional)',
      pathPlaceholder: 'Ruta completa a ffmpeg',
      pathHelp: 'Déjalo vacío para que Arcanist lo busque automáticamente.',
      check: 'Comprobar',
      works: 'Funciona: ffmpeg {version}.',
    },
    ratings: {
      explanation:
        'Los usuarios valoran las canciones para sus StarTracks, sus mixes y sus estadísticas. Elige cómo:',
      stars: 'De una a cinco estrellas',
      starsHelp: 'Más matices. Cinco estrellas es un StarTrack.',
      star: 'StarTrack',
      starHelp: 'Más simple: una canción es un StarTrack o no.',
      later:
        'Puedes cambiarlo en Ajustes cuando quieras, sin perder nada: los StarTracks pasan a ser cinco estrellas y al revés.',
    },
    privacy: {
      noTelemetry: 'Desactivar toda telemetría',
      noTelemetryHelp:
        'No se envían estadísticas anónimas, y las cuentas de Ultimate Cloud vinculadas empiezan sin enviar reproducciones (cada uno puede activarlas).',
      noExternal: 'Desactivar todo servicio externo',
      noExternalHelp:
        'Ni servicios en línea ni Ultimate Cloud: su extensión se apaga. Nada sale del servidor.',
      cloudOnly: 'Desactivar todo servicio externo excepto Ultimate Cloud',
      cloudOnlyHelp: 'Sin MusicBrainz, AcoustID, Cover Art Archive ni LRCLib.',
      streamingUnaffected:
        'No afecta a los servicios del propio servidor: DLNA, Subsonic, mStream ni la red P2P.',
      explanation:
        'Arcanist puede aportar recuentos anónimos de reproducción (qué canciones se escuchan, sin datos personales ni nombres de archivo) a las estadísticas globales de la red Ultimate, que alimentan rankings y recomendaciones para todos.',
      yes: 'Sí, compartir estadísticas anónimas',
      no: 'No, mantener todo en este servidor',
      choose: 'Elige una opción.',
      later: 'Podrás cambiarlo en cualquier momento desde los ajustes.',
      online: 'Servicios en línea',
      onlineExplanation:
        'Arcanist puede completar tu biblioteca consultando servicios gratuitos. Solo se envían huellas acústicas, identificadores y nombres de artista, álbum y canción; nunca rutas, usuarios ni lo que escuchas.',
      services: {
        musicBrainz: { name: 'MusicBrainz', help: 'Identifica tus álbumes, su año y su artista.' },
        acoustId: {
          name: 'AcoustID',
          help: 'Reconoce canciones por cómo suenan, aunque no tengan etiquetas.',
        },
        coverArtArchive: { name: 'Cover Art Archive', help: 'Añade las carátulas que falten.' },
        lyrics: { name: 'LRCLib', help: 'Busca la letra de las canciones que no la traigan.' },
      },
    },
    review: {
      privacy: 'Privacidad',
      privacyDefault: 'Sin restricciones',
      users: 'Otros usuarios',
      noUsers: 'Ninguno',
      p2p: 'Red P2P',
      on: 'Activada',
      off: 'Apagada',
      addons: 'Extensiones',
      noAddons: 'Ninguna',
      extensionUrls: '{count} por dirección',
      player: 'reproductor web',
      existing: 'ya existe',
      import: 'Importar',
      importNone: 'Nada',
      mstreamWithUsers: 'mStream: {count} biblioteca(s), con sus usuarios',
      mstreamWithoutUsers: 'mStream: {count} biblioteca(s), sin usuarios',
      database: 'Base de datos',
      admin: 'Administrador',
      libraries: 'Bibliotecas',
      folders: '{count} carpeta(s)',
      noLibraries: 'Ninguna por ahora',
      statistics: 'Estadísticas anónimas',
      ffmpegLater: 'Más tarde, desde Ajustes',
      ratings: 'Valoraciones',
      noOnlineServices: 'Ninguno',
      afterwards:
        'Al terminar verás cómo Arcanist lee tu música y, si importas, cómo llegan los datos.',
    },
    progress: {
      migrationWait: 'Espera a que termine la importación.',
      extrasFailed: 'Esto no se pudo aplicar; puedes hacerlo desde el panel:',
      libraries: 'Bibliotecas',
      mstream: 'Importación desde mStream',
      arcanist: 'Migración desde otro Arcanist',
      dlna: 'Copia desde un servidor DLNA',
      created: '{libraries} biblioteca(s) y {users} usuario(s) creados.',
      brought:
        '{playlists} playlists, {ratings} valoraciones y {plays} canciones reproducidas · {unmatched} sin encontrar.',
      background: 'Puedes ir al panel cuando quieras: esto seguirá en segundo plano.',
      done: 'Todo listo. Tu música te espera.',
      open: 'Ir al panel',
    },
  },
  overview: {
    title: 'Hola, {name}',
    subtitle: 'Resumen de tu servidor.',
    libraries: 'Bibliotecas',
    artists: 'Artistas',
    albums: 'Álbumes',
    tracks: 'Canciones',
    noLibraries: 'Aún no hay bibliotecas',
    noLibrariesHelp: 'Añade las carpetas donde está tu música para empezar.',
    sourceCode: 'Código fuente',
    license: 'Software libre bajo la licencia AGPL-3.0-or-later.',
    madeBy: 'Un producto open source de LinkFast.',
  },
  duplicates: {
    title: 'Posibles duplicados',
    subtitle:
      'Canciones que suenan igual pero no son claramente la misma. Conserva una para unir sus copias en una sola canción, o mantén ambas.',
    similarity: '{percent} % de parecido',
    keepThis: 'Conservar esta',
    keepBoth: 'Son distintas',
    number: 'Pista {number}',
    merged: 'Canciones unidas.',
    none: 'No hay posibles duplicados',
    noneHelp:
      'Arcanist compara la huella acústica de cada canción al analizarla. Las copias evidentes se unen solas; aquí solo aparecen los casos dudosos.',
  },
  settings: {
    openSource: {
      title: 'Open source',
      description:
        'Qué partes de este servidor no son de código abierto, y el modo que solo deja usar las que sí lo son.',
      switch: 'Modo 100% open source',
      help: 'Las extensiones, reproductores y herramientas de código cerrado (por su licencia) no se instalan ni funcionan mientras esté activo.',
      byEnvironment:
        'Lo fija la configuración del servidor (variables de entorno): no se puede cambiar aquí.',
      edition: 'Edición del servidor:',
      editions: { OPEN_SOURCE: 'OPENSOURCE', STANDARD: 'STANDARD', ULTIMATE: 'ULTIMATE' },
      closed: 'Componentes de código cerrado instalados',
      noneClosed: 'No hay ningún componente de código cerrado instalado.',
      kinds: { EXTENSION: 'Extensión', PLAYER: 'Reproductor web', TOOL: 'Herramienta' },
      active: 'En uso',
      inactive: 'Sin usar',
      blocked: 'Bloqueado',
      offNote: 'Una extensión que estaba bloqueada vuelve a funcionar tras reiniciar el servidor.',
      on: 'Modo open source activado',
      off: 'Modo open source desactivado',
    },
    privacy: {
      title: 'Privacidad',
      description:
        'Qué sale de este servidor. Lo que se excluye se apaga y no se puede encender mientras siga excluido.',
      saved: 'Privacidad guardada',
    },
    statistics: {
      title: 'Estadísticas anónimas',
      description:
        'Qué canciones se escuchan en este servidor, sin decir quién, para los rankings globales de Ultimate Cloud.',
      share: 'Enviar estadísticas anónimas',
      help: 'Solo se envían el identificador AcoustID de cada canción, su título, artista y álbum, y cuándo se escuchó; nunca usuarios ni rutas. No hace falta una cuenta de Ultimate Cloud. Lo que se envía a la cuenta vinculada (tus reproducciones, StarTracks) se elige en la página Ultimate Cloud.',
      saved: 'Ajuste guardado',
    },
    maintenance: {
      title: 'Mantenimiento',
      description:
        'Volver a configurar el servidor o restablecerlo. Tus archivos de música nunca se tocan.',
      wizard: 'Ejecutar el asistente de nuevo',
      wizardHelp:
        'Repasa el idioma, las bibliotecas, ffmpeg, las valoraciones, los servicios en línea y las estadísticas anónimas sin perder nada.',
      wizardRun: 'Abrir el asistente',
      word: 'restablecer',
      type: 'Para confirmar, escribe {word}:',
      restarting: 'Restableciendo: el servidor se reinicia y abrirá el asistente inicial…',
      settings: {
        title: 'Restablecer los ajustes',
        help: 'Ajustes del servidor a sus valores iniciales: valoraciones, servicios en línea, DLNA, mStream, red P2P, extensiones… Las bibliotecas, los usuarios y sus datos se conservan.',
        button: 'Restablecer ajustes',
        confirmTitle: '¿Restablecer los ajustes?',
        confirm:
          'Los ajustes vuelven a como estaban al instalar Arcanist. Bibliotecas, usuarios, valoraciones y playlists no cambian.',
        done: 'Ajustes restablecidos',
      },
      userData: {
        title: 'Borrar los datos de los usuarios',
        help: 'Valoraciones, reproducciones, historial y playlists de todos los usuarios. Las bibliotecas, los usuarios y los ajustes se conservan.',
        button: 'Borrar datos',
        confirmTitle: '¿Borrar los datos de todos los usuarios?',
        confirm:
          'Se borran las valoraciones, las reproducciones, el historial y las playlists de todos. No se puede deshacer. Los StarTracks de Ultimate Cloud no se tocan.',
        done: 'Datos de los usuarios borrados',
      },
      factory: {
        title: 'Restablecimiento de fábrica',
        help: 'Todo vuelve a como recién instalado: base de datos, usuarios, ajustes, claves y caché. La cuenta de Ultimate Cloud se desvincula y el servidor se reinicia en el asistente inicial.',
        button: 'Restablecer de fábrica',
        confirmTitle: '¿Restablecer Arcanist de fábrica?',
        confirm:
          'Se borran la base de datos (usuarios, bibliotecas, valoraciones, playlists…), los ajustes, las claves y la caché, y el servidor se reinicia en el asistente inicial. Tus archivos de música no se tocan. No se puede deshacer.',
        addons:
          'Quitar también los reproductores y extensiones instalados y las herramientas descargadas (ffmpeg, ultimate-audio)',
      },
    },
    ratings: {
      title: 'Valoraciones',
      description: 'Cómo califican los usuarios las canciones en todos los reproductores.',
      stars: 'De una a cinco estrellas',
      star: 'StarTrack',
      help: 'Al cambiar no se pierde nada: los StarTracks pasan a ser cinco estrellas y al revés, y las demás valoraciones se guardan para cuando vuelvas a las cinco estrellas.',
      saved: 'Modo de valoración guardado.',
    },
    title: 'Ajustes',
    subtitle: 'Herramientas y servicios del servidor.',
    lyricsTiming: {
      title: 'Letras con tiempos',
      description:
        'Las letras con el momento de cada línea, para seguirlas mientras suena la canción.',
      counts: '{timed} de {all} letras tienen tiempos ({aligned} calculados en este servidor).',
      left: '{count} letras siguen sin tiempos.',
      offer:
        'La extensión oficial Lyrics Sync los calcula en este servidor a partir de la propia canción (separa la voz y la reconoce con Whisper). Descarga unos 340 MB y trabaja en segundo plano con prioridad baja.',
      install: 'Instalar Lyrics Sync',
      installed: 'Lyrics Sync está instalada pero apagada o esperando un reinicio:',
      open: 'Abrir Lyrics Sync',
    },
    online: {
      restricted: 'Desactivados por la privacidad elegida (Ajustes › Privacidad).',
      title: 'Servicios en línea',
      description:
        'Completan tu biblioteca en segundo plano, a una consulta por segundo. Solo envían huellas acústicas, identificadores y nombres de artista, álbum y canción.',
      service: {
        MUSIC_BRAINZ: { name: 'MusicBrainz', help: 'Identifica los álbumes, su año y su artista.' },
        ACOUST_ID: { name: 'AcoustID', help: 'Reconoce canciones por su huella acústica.' },
        COVER_ART_ARCHIVE: {
          name: 'Cover Art Archive',
          help: 'Añade las carátulas que falten a los álbumes identificados.',
        },
        LYRICS: { name: 'LRCLib', help: 'Busca letras para las canciones que no las traen.' },
      },
      found: '{count} encontrados',
      notFound: '{count} sin resultado',
      pending: '{count} pendientes',
      lastRequest: 'última consulta {when}',
      needsKey: 'Falta una clave de AcoustID',
      error: {
        connection_failed: 'Sin conexión con el servicio',
        timeout: 'El servicio no respondió a tiempo',
        rate_limited: 'El servicio pidió esperar',
        invalid_key: 'La clave de AcoustID no es válida',
        service_error: 'El servicio devolvió un error',
        unexpected_response: 'Respuesta inesperada del servicio',
        unexpected_error: 'Error inesperado',
      },
      acoustIdKey: 'Clave de AcoustID propia (opcional)',
      keyPlaceholder: 'Clave de aplicación',
      keySaved: 'Usando tu clave; escribe otra para cambiarla',
      acoustIdKeyNeeded:
        'AcoustID necesita una clave de aplicación, gratuita: regístrala en acoustid.org/new-application y pégala aquí.',
      acoustIdKeyHelp:
        'Arcanist trae su propia clave. Si prefieres usar la tuya, regístrala gratis en acoustid.org/new-application. Guarda el campo vacío para volver a la de Arcanist.',
    },
    ffmpeg: {
      outdatedTitle: 'ffmpeg antiguo',
      outdated:
        'Este ffmpeg ({version}) es más antiguo que el recomendado ({recommended} o posterior): algunas conversiones pueden fallar o ser más lentas. Descarga el de Arcanist; se usará en lugar del del sistema.',
      outdatedNoDownload:
        'Este ffmpeg ({version}) es más antiguo que el recomendado ({recommended} o posterior). Instala una versión más nueva en el sistema o indica su ruta.',
      remove: 'Borrar el ffmpeg descargado',
      removeConfirm: '¿Borrar el ffmpeg que descargó Arcanist?',
      features: {
        title: 'Características adicionales activas',
        none: 'Ninguna. HE-AAC necesita un ffmpeg con libfdk_aac (o el de macOS, con aac_at).',
        guide: 'Cómo instalar uno',
        HE_AAC: 'HE-AAC',
      },
      removeHelp: 'Se usará el configurado o el del sistema, si hay.',
      description:
        'Se usa para convertir canciones a formatos más ligeros y para analizar los formatos que Arcanist no lee por sí mismo (Opus, WMA…).',
      available: 'Disponible',
      missing: 'No disponible',
      source: {
        CONFIGURED: 'ruta configurada',
        DOWNLOADED: 'descargado por Arcanist',
        SYSTEM: 'del sistema',
      },
      configuredBroken: 'La ruta configurada ({path}) no es un ffmpeg que funcione.',
      path: 'Ruta de ffmpeg',
      automatic: 'Automática',
      pathHelp: 'Déjala vacía para que Arcanist lo busque automáticamente.',
      download: 'Descargar ffmpeg',
      update: 'Descargar la última versión',
      downloadHelp:
        'Una build estática de BtbN/FFmpeg-Builds (GPL) se guarda en la carpeta de datos de Arcanist.',
      downloading: 'Descargando ffmpeg: {received} de {total}…',
      installed: 'ffmpeg {version} instalado.',
    },
  },
  analysis: {
    title: 'Análisis de audio',
    description:
      'Arcanist mide el volumen, la huella acústica, la forma de onda, el tempo y la tonalidad de cada canción en segundo plano.',
    enabled: 'Activado',
    activity: {
      DISABLED: 'Desactivado',
      UNAVAILABLE: 'No disponible',
      IDLE: 'Al día',
      WAITING_FOR_SCAN: 'Esperando a que termine el escaneo',
      RUNNING: 'Analizando',
    },
    speed: '{count} canciones por minuto',
    analyzed: 'Analizadas',
    pending: 'Pendientes',
    needsFfmpeg: 'Necesitan ffmpeg',
    failed: 'Con errores',
    unavailable: 'Esta instalación no incluye el módulo de análisis de audio.',
    needsFfmpegHelp:
      'Algunos formatos (como Opus o WMA) se analizarán cuando ffmpeg esté disponible.',
  },
  libraries: {
    managed: 'Gestionada',
    managedHelp:
      'La mantiene una extensión gestionada del servidor (por ejemplo, el almacenamiento de un servicio alojado): se usa como cualquier otra, pero no se puede eliminar ni cambiar su ubicación.',
    personal: 'Personal',
    personalHelp:
      'Tu música de Ultimate Cloud: solo la ves tú y desaparece al desvincular tu cuenta.',
    disabled: 'Desactivada',
    disabledHelp:
      'Su música está en Ultimate Cloud o S3 y la extensión Ultimate Cloud está desactivada: solo se puede eliminar. Activa la extensión en Extensiones para volver a usarla.',
    sharedByServer: 'Compartida por {server} a través de la red P2P',
    summary: {
      holds: '{tracks} canciones · {albums} álbumes · {size} · {duration}',
      hoursMinutes: '{hours} h {minutes} min',
      minutes: '{minutes} min',
      listened: '{plays} reproducciones y {listeners} oyentes en 30 días',
      more: 'Ver estadísticas',
    },
    sharedByPeer: 'Compartida por otro servidor a través de la red P2P',
    ultimateCloud: {
      Library: 'Tu biblioteca de Ultimate Cloud',
      CloudDrop: 'Tu Cloud Drop de Ultimate Cloud',
    },
    title: 'Bibliotecas',
    subtitle: 'Carpetas de música y su análisis.',
    create: 'Nueva biblioteca',
    edit: 'Editar biblioteca',
    name: 'Nombre',
    folders: 'Carpetas',
    folder: 'Carpeta',
    addFolder: 'Elegir carpeta',
    changeFolder: 'Cambiar carpeta',
    visibleToAll: 'Visible para todos los usuarios',
    everyone: 'Todos',
    restricted: 'Acceso restringido',
    interval: 'Escanear automáticamente cada',
    intervalHelp: 'Entre 5 minutos y 30 días. Déjalo vacío para escanear solo a mano.',
    intervalUnit: 'Unidad',
    units: { MINUTES: 'minutos', HOURS: 'horas', DAYS: 'días' },
    watching: 'vigilando cambios',
    watch: {
      FOLDER: {
        label: 'Vigilar cambios en la carpeta',
        help: 'Lo que se añada, cambie o quite se escanea al momento, cuando los archivos dejan de cambiar. El intervalo sigue siendo útil para lo que la vigilancia no vea.',
        warning:
          'Vigilar tiene un coste: en carpetas muy grandes usa memoria y, en Linux, un vigilante de inotify por carpeta (si se agotan, sube fs.inotify.max_user_watches). En carpetas de red (SMB/NFS) puede no avisar de nada o hacerlo sin parar; ahí es mejor un intervalo.',
      },
      CLOUD: {
        label: 'Recibir los cambios de Ultimate Cloud',
        help: 'Ultimate Cloud avisa al servidor cuando cambia tu música, y se escanea al momento. Sin coste para el servidor.',
      },
    },
    lastScan: 'Último escaneo',
    neverScanned: 'Aún no escaneada',
    every: {
      MINUTES: 'cada {count} min',
      HOURS: 'cada {count} h',
      DAYS: 'cada {count} d',
    },
    empty: 'No hay bibliotecas',
    emptyHelp: 'Crea una biblioteca a partir de las carpetas donde guardas tu música.',
    emptyNoAccess: 'Todavía no tienes acceso a ninguna biblioteca. Pídeselo a un administrador.',
    saved: 'Biblioteca guardada',
    folderAdded: 'Carpeta añadida; se está escaneando',
    deleted: 'Biblioteca «{name}» eliminada',
    deleteTitle: '¿Eliminar «{name}»?',
    deleteHelp:
      'Se quitará del catálogo junto con sus canciones. Los archivos en disco no se tocan.',
    move: {
      button: 'Mover la carpeta',
      title: 'Mover la carpeta de la biblioteca',
      help: 'Sus archivos se copian a la carpeta nueva (con las mismas subcarpetas y fechas), la biblioteca pasa a leer de ahí y al final se vacía la carpeta anterior. Sus canciones, valoraciones y playlists siguen igual. No se sobrescribe nada.',
      from: 'Carpeta actual',
      to: 'Carpeta nueva',
      toHelp:
        'Una carpeta del servidor, fuera de la actual. Si un traslado se interrumpió, elige la misma: lo ya copiado no se vuelve a copiar.',
      choose: 'Elegir…',
      keep: 'Solo copiar',
      keepHelp: 'La carpeta actual conserva sus archivos; la biblioteca lee de la nueva.',
      cleanHelp: 'Cuando todo esté copiado, los archivos se borran de la carpeta actual.',
      start: 'Mover',
      copy: 'Copiar',
      started: 'Moviendo la carpeta en segundo plano',
      done: 'La biblioteca ahora lee de {to}',
      failed: 'El traslado se detuvo: {error}',
      phase: {
        COPYING: 'Copiando a {to}: {done} de {total} archivos ({bytes} de {of})',
        SWITCHING: 'Cambiando la biblioteca a {to}',
        CLEANING: 'Vaciando la carpeta anterior',
      },
    },
  },
  scan: {
    start: 'Escanear',
    full: 'Escaneo completo',
    cancel: 'Cancelar escaneo',
    queued: 'En cola…',
    progress: '{read} de {total} leídos ({found} encontrados)',
    summary: '{seen} archivos: {added} nuevos, {updated} actualizados, {missing} desaparecidos',
    unreadable: '{count} archivo(s) no se pudieron leer',
    phase: {
      DISCOVERING: 'Buscando archivos…',
      READING: 'Leyendo etiquetas…',
      CLEANING_UP: 'Limpiando el catálogo…',
    },
    status: {
      QUEUED: 'En cola',
      RUNNING: 'Escaneando',
      COMPLETED: 'Escaneo completado',
      CANCELLED: 'Escaneo cancelado',
      FAILED: 'El escaneo falló',
    },
  },
  users: {
    title: 'Usuarios',
    subtitle: 'Cuentas, permisos y acceso a bibliotecas.',
    create: 'Nuevo usuario',
    edit: 'Editar usuario',
    tabs: { users: 'Usuarios', access: 'Acceso a bibliotecas' },
    role: 'Rol',
    roles: { USER: 'Usuario', ADMIN: 'Administrador' },
    permissions: 'Permisos',
    libraries: 'Bibliotecas',
    lastLogin: 'Último acceso',
    never: 'Nunca',
    disabled: 'Desactivado',
    allPermissions: 'Todos',
    listenOnly: 'Solo escuchar',
    adminAccess: 'Todas',
    disableAccount: 'Desactivar la cuenta',
    accessHelp:
      'Qué puede hacer cada usuario en cada biblioteca: escucharla, o escucharla y subirle música (subir necesita además el permiso «Subir música»). Las visibles para todos las escucha cualquiera.',
    noRestrictedLibraries: 'Todas las bibliotecas son visibles para todos los usuarios.',
    accessSummary: '{count} de {total} bibliotecas',
    accessListenOnly: '{listen} solo escuchar',
    editAccess: 'Editar acceso',
    accessFor: 'Acceso de {name}',
    accessSaved: 'Acceso de {name} guardado',
    searchUsers: 'Buscar usuarios',
    noUsersMatch: 'Ningún usuario coincide.',
    accessEditor: {
      search: 'Buscar bibliotecas',
      all: 'Todas:',
      allShown: 'Las que se ven:',
      none: 'Ninguna biblioteca coincide.',
      summary: 'Accede a {count} de {total} bibliotecas.',
      summaryListen: 'Accede a {count} de {total} bibliotecas; en {listen} solo escucha.',
    },
    noLibraries: 'Todavía no hay bibliotecas.',
    visibleToAll: 'Visible para todos',
    access: { none: 'Sin acceso', listen: 'Escuchar', upload: 'Escuchar y subir' },
    resetPassword: 'Restablecer contraseña',
    resetPasswordFor: 'Nueva contraseña para {name}',
    resetPasswordHelp: 'Se cerrarán todas sus sesiones abiertas.',
    passwordReset: 'Contraseña de {name} restablecida',
    saved: 'Usuario guardado',
    deleted: 'Usuario {name} eliminado',
    deleteTitle: '¿Eliminar a {name}?',
    deleteHelp: 'Se eliminará la cuenta y sus sesiones. No se puede deshacer.',
  },
  permissions: {
    MANAGE_LIBRARIES: 'Gestionar bibliotecas',
    MANAGE_USERS: 'Gestionar usuarios',
    MANAGE_SETTINGS: 'Gestionar ajustes',
    RUN_SCANS: 'Escanear',
    DOWNLOAD: 'Descargar',
    VIEW_ALL_STATS: 'Ver todas las estadísticas',
    UPLOAD: 'Subir música',
    PLAY_ON_SERVER: 'Reproducir en el servidor',
    VIEW_ALL_WISHES: 'Ver la lista de deseos de todos',
    EDIT_METADATA: 'Editar etiquetas y carátulas',
    DELETE_TRACKS: 'Eliminar canciones',
  },
  permissionHelp: {
    MANAGE_LIBRARIES: 'Crear, editar y eliminar las bibliotecas a las que tiene acceso.',
    MANAGE_USERS: 'Crear y editar usuarios con, como mucho, sus mismos permisos.',
    MANAGE_SETTINGS: 'Cambiar los ajustes del servidor.',
    RUN_SCANS: 'Lanzar y cancelar escaneos.',
    DOWNLOAD: 'Descargar los archivos originales.',
    VIEW_ALL_STATS: 'Ver las estadísticas de todos los usuarios.',
    UPLOAD:
      'Añadir música a las bibliotecas a las que tiene acceso, desde las apps (por ejemplo, al migrar la biblioteca de Ultimate Desktop).',
    PLAY_ON_SERVER:
      'Reproducir en el dispositivo de audio del servidor (Audio del servidor) desde los reproductores y Ultimate Connect.',
    VIEW_ALL_WISHES:
      'Ver las canciones que quieren todos los usuarios (la lista de deseos del servidor), por ejemplo para conseguirlas.',
    EDIT_METADATA:
      'Cambiar las etiquetas (título, artistas, álbum…) y las carátulas de las canciones de las bibliotecas a las que sube música, desde apps como Ultimate Desktop. Se escriben en los propios archivos; el original va a la papelera de la carpeta.',
    DELETE_TRACKS:
      'Eliminar canciones de las bibliotecas a las que sube música, desde las apps. Sus archivos van a la papelera de la carpeta (30 días).',
  },
  account: {
    videos: {
      title: 'Videoclips',
      description:
        'Qué audio oyes con los videoclips. Los reproductores también permiten cambiarlo mientras suenan.',
      audio: 'Audio por defecto',
      server: 'El del servidor ({mode})',
    },
    dlna: {
      title: 'Red local (DLNA)',
      description:
        'Los televisores, altavoces y apps de la red local ven «{name}» sin iniciar sesión, con las bibliotecas que publica el administrador.',
      show: 'Mostrar mis StarTracks en la red local (solo canciones de las bibliotecas publicadas)',
    },
    cloud: {
      title: 'Mi Ultimate Cloud',
      description: 'Vincula tu propia cuenta de Ultimate Cloud con este servidor.',
      notes: {
        library: 'Tu música de Ultimate Cloud aparece aquí como una biblioteca que solo ves tú.',
        listening:
          'Tus reproducciones, estadísticas y StarTracks se comparten con tus apps de Ultimate.',
        devices:
          'Tus dispositivos de Ultimate se conectan a este servidor por la red P2P sin emparejarlos.',
      },
      link: 'Vincular mi cuenta',
      library: 'Biblioteca',
      libraryReady: 'Mi Ultimate Cloud, solo para ti',
      libraryNone: 'Preparando…',
      topSong: 'Canción más escuchada: {name} — {by} ({plays})',
      topArtist: 'Artista más escuchado: {name} ({plays})',
      unlinkTitle: '¿Desvincular tu cuenta de Ultimate Cloud?',
      unlinkHelp:
        'Tu biblioteca de Ultimate Cloud desaparece de este servidor y dejan de enviarse tus reproducciones y StarTracks. En Ultimate Cloud no se borra nada.',
      services: {
        personalPlays: {
          name: 'Mis reproducciones',
          help: 'Lo que escuchas aquí cuenta en tus estadísticas y en los rankings de Ultimate Cloud.',
        },
        albumPlaythroughs: {
          name: 'Álbumes completos',
          help: 'Los álbumes que escuchas de principio a fin alimentan los rankings de álbumes.',
        },
        starTracks: {
          name: 'StarTracks',
          help: 'Tus StarTracks de aquí y los de tus apps Ultimate son los mismos, en los dos sentidos.',
        },
        metrics: {
          name: 'Mis estadísticas',
          help: 'Muestra aquí lo que Ultimate Cloud cuenta de tu música.',
        },
      },
    },
    appPasswords: {
      title: 'Contraseñas de aplicación',
      description:
        'Para apps de Subsonic (Symfonium, DSub, Feishin…) y de mStream. Cada app usa la suya y tu contraseña nunca sale de aquí.',
      name: 'Nombre',
      namePlaceholder: 'Por ejemplo: Symfonium en el móvil',
      create: 'Crear',
      created: 'Copia la contraseña ahora: no se volverá a mostrar.',
      howTo:
        'En la app, usa {server} como servidor, {user} como usuario y esta contraseña. Las apps de OpenSubsonic también la aceptan como clave de API.',
      createdAt: 'creada',
      neverUsed: 'sin usar',
      revoke: 'Revocar',
      none: 'Aún no has creado ninguna.',
    },
    title: 'Mi cuenta',
    changePassword: 'Cambiar contraseña',
    changePasswordHelp: 'Se cerrarán tus sesiones en otros dispositivos.',
    currentPassword: 'Contraseña actual',
    newPassword: 'Contraseña nueva',
    passwordChanged: 'Contraseña cambiada',
    sessions: 'Sesiones abiertas',
    sessionsHelp: 'Dispositivos donde has iniciado sesión.',
    unknownDevice: 'Dispositivo desconocido',
    thisDevice: 'Este dispositivo',
    lastSeen: 'última actividad',
    signOut: 'Cerrar sesión',
    about: 'Acerca de',
  },
  folderPicker: {
    title: 'Elegir carpeta del servidor',
    typePath: 'Escribe una ruta, p. ej. /mnt/musica o D:\\Música',
    go: 'Ir',
    up: 'Subir',
    showHidden: 'Mostrar ocultas',
    roots: 'Unidades y raíces',
    empty: 'Sin subcarpetas',
    select: 'Usar esta carpeta',
    newFolder: 'Nueva carpeta',
    newFolderName: 'Nombre de la nueva carpeta',
    create: 'Crear',
  },
  import: {
    uploads: {
      defaultLibrary: 'Las subidas sin biblioteca van a',
      noDefault: 'Ninguna (cada subida elige)',
      defaultHelp:
        'Para las apps y extensiones que suben sin elegir biblioteca, y los deseos en automático.',
      defaultSaved: 'Destino de las subidas guardado.',
      title: 'Subidas',
      description:
        'Todo lo que se sube a las bibliotecas, lo envíe quien lo envíe: desde aquí, Ultimate Desktop, los reproductores web, el móvil o una extensión. Todo pasa por la misma API, se coloca con la plantilla de la biblioteca y se escanea poco después.',
      refresh: 'Actualizar',
      send: 'Subir música',
      sendHelp:
        'Los archivos entran en la biblioteca elegida con su plantilla de carpetas, sin sobrescribir nada; lo que ya tiene se marca como repetido.',
      library: 'Biblioteca',
      noWritable:
        'No hay bibliotecas donde puedas subir (necesitan una carpeta local o S3 con permiso de escritura).',
      choose: 'Elige archivos o suéltalos aquí',
      formats: 'FLAC, MP3, M4A, Opus, Ogg, WAV, AIFF…',
      start: 'Subir {count}',
      background: 'Seguir en segundo plano',
      origin: 'Origen',
      status: 'Estado',
      anyOrigin: 'Cualquier origen',
      anyStatus: 'Cualquier estado',
      anyLibrary: 'Todas las bibliotecas',
      count: '{count} subidas',
      active: '{count} esperando su escaneo',
      none: 'Todavía no se ha subido nada.',
      more: 'Ver más',
      origins: {
        PANEL: 'Panel',
        DESKTOP: 'Ultimate Desktop',
        WEB: 'Reproductor web',
        MOBILE: 'Móvil',
        EXTENSION: 'Extensión',
        API: 'API',
      },
      statuses: {
        PROCESSING: 'Procesando',
        ADDED: 'Añadida',
        DUPLICATE: 'Repetida',
        NOT_ADDED: 'No añadida',
        FAILED: 'Fallida',
        QUEUED: 'En cola',
        UPLOADING: 'Subiendo',
        UPLOADED: 'Subida',
        ALREADY_IN_CLOUD: 'Ya estaba',
      },
      sending: {
        waiting: 'En espera',
        sending: 'Subiendo…',
        imported: 'Subida',
        duplicate: 'Ya estaba',
        failed: 'Falló',
      },
    },
    title: 'Importación',
    subtitle:
      'Trae música y datos a Arcanist: desde otro servidor, o dejando archivos en una carpeta de entrada.',
    migrate: {
      title: 'Migrar desde otro servidor',
      help: 'Trae bibliotecas, usuarios, playlists, valoraciones y reproducciones de otro servidor de música. El otro servidor no se modifica.',
      mstream: 'Desde mStream',
      dlna: 'Desde un servidor DLNA',
      arcanist: 'Desde otro Arcanist',
      clear: 'Limpiar historial',
      clearTitle: '¿Limpiar el historial de importaciones?',
      clearHelp:
        'Solo se quita el historial de las importaciones terminadas: la música, los usuarios y sus datos se quedan. Las importaciones de un servidor mStream cuyos usuarios aún no han iniciado sesión se mantienen, porque las necesitan para entrar.',
      clearConfirm: 'Limpiar',
      cleared: 'Historial limpiado: {count} importación(es)',
      remove: 'Quitar del historial',
      removeHelp: 'Solo se quita del historial: lo importado se queda.',
    },
    folders: {
      title: 'Carpetas de entrada',
      help: 'Lo que copies en ellas se identifica por sus etiquetas y se mueve a la biblioteca cuando termina de copiarse.',
      add: 'Añadir carpeta',
      edit: 'Editar carpeta de entrada',
      empty: 'Aún no hay carpetas de entrada.',
      noLibraries: 'Crea primero una biblioteca a la que importar.',
      into: 'A {library}',
      convertsTo: 'convierte a {format}',
      copies: 'copia (deja los originales)',
      cloudDrop: 'Cloud Drop',
      disabled: 'Desactivada: necesita Ultimate Cloud, que está apagado',
      uploadToCloud: 'Subir a Ultimate Cloud lo que se identifique',
      uploadToCloudHelp:
        'Cuando una canción traída de Cloud Drop ya está en la biblioteca e identificada (AcoustID o MusicBrainz, en Ajustes > Servicios en línea), se sube a la biblioteca de Ultimate Cloud elegida, con la descripción de Arcanist. Lo que no se identifica no se sube.',
      uploads: 'sube lo identificado a {target}',
      uploadTarget: 'Subir a',
      uploadTargetHelp:
        'La biblioteca de una cuenta de Ultimate Cloud vinculada a este servidor. Si esa cuenta deja de estar vinculada, no se sube nada hasta que vuelva.',
      unlinkedAccount: 'una cuenta que ya no está vinculada',
      kind: 'Origen',
      kinds: {
        FOLDER: 'Una carpeta del servidor',
        ULTIMATE_CLOUD_DROP: 'Cloud Drop de Ultimate Cloud',
      },
      cloudDropHelp:
        'Las canciones que subas a Cloud Drop (sin identificar) llegan aquí y se importan con las mismas reglas: se identifican, se colocan con la plantilla y se omiten las repetidas.',
      cloudModeHelp: {
        MOVE: 'Una vez en la biblioteca, las canciones salen de Cloud Drop.',
        COPY: 'Las canciones se quedan también en Cloud Drop; cada una se trae una sola vez.',
      },
      mode: 'Al importar',
      modes: { MOVE: 'Mover a la biblioteca', COPY: 'Copiar a la biblioteca' },
      modeHelp: {
        MOVE: 'Los archivos salen de la carpeta de entrada (a su papelera, 30 días).',
        COPY: 'Los archivos se quedan donde están y se importa una copia; se vuelven a importar solo si cambian.',
      },
      paused: 'En pausa',
      unavailable: 'No disponible',
      path: 'Carpeta',
      pathHelp:
        'Una carpeta del servidor fuera de las bibliotecas. Arcanist necesita poder escribir en ella.',
      target: 'Biblioteca de destino',
      targetHelp: 'Los archivos se colocan en esta ubicación según la plantilla de la biblioteca.',
      policy: 'Si la canción ya está en la biblioteca',
      convert: 'Convertir al importar',
      convertNone: 'No convertir',
      convertHelp: 'Los archivos sin pérdida se convierten a este formato. Necesita ffmpeg.',
      convertFlacHelp:
        'WAV, AIFF, APE y otros formatos sin pérdida se guardan como FLAC, sin perder calidad. Necesita ffmpeg.',
      bitRate: 'Bitrate (kbit/s)',
      bitRateDefault: 'Por defecto',
      convertLossy: 'Convertir también archivos con pérdida en otro formato',
      convertLossyHelp:
        'Volver a comprimir un MP3 u otro formato con pérdida reduce algo su calidad.',
      enabled: 'Importar desde esta carpeta',
      saved: 'Carpeta de entrada guardada.',
      deleteTitle: '¿Eliminar esta carpeta de entrada?',
      deleteHelp:
        'Arcanist dejará de importar desde ella y olvidará su historial. Los archivos que contenga no se tocan.',
    },
    policies: {
      SKIP: 'Omitirla',
      KEEP_BEST: 'Quedarse con la de mejor calidad',
      ADD_VERSION: 'Añadirla como otra copia',
    },
    policyHelp: {
      SKIP: 'El archivo se queda en la carpeta de entrada, en la lista de duplicados.',
      KEEP_BEST:
        'Si el archivo nuevo tiene mejor calidad, sustituye a las copias de la biblioteca, que van a la papelera durante 30 días.',
      ADD_VERSION: 'La pista tendrá las dos copias y al reproducir se elige la mejor.',
    },
    queue: {
      title: 'Archivos encontrados',
      help: 'Lo que no se pudo colocar solo espera aquí a que decidas.',
      pending: 'Procesando {count} archivo(s)…',
      processing: 'Procesando…',
      importedAs: 'Importado como {path}',
    },
    status: {
      PENDING: 'Pendientes',
      NEEDS_REVIEW: 'Por revisar',
      DUPLICATE: 'Duplicados',
      FAILED: 'Con errores',
      IGNORED: 'Ignorados',
      IMPORTED: 'Importados',
      DISCARDED: 'Descartados',
    },
    empty: {
      PENDING: 'No hay nada pendiente.',
      NEEDS_REVIEW: 'No hay archivos por revisar.',
      DUPLICATE: 'No hay duplicados.',
      FAILED: 'No hay errores.',
      IGNORED: 'No hay archivos ignorados.',
      IMPORTED: 'No se ha importado nada en los últimos 30 días.',
      DISCARDED: 'No hay archivos descartados.',
    },
    reasons: {
      MISSING_TAGS: 'Faltan título, artista o álbum',
      SAME_AUDIO: 'Ya está en la biblioteca',
      SAME_TRACK: 'La canción ya está en la biblioteca',
      NOT_BETTER: 'La biblioteca ya la tiene con igual o mejor calidad',
      REPLACED_WORSE: 'Sustituyó a copias de peor calidad',
      UNREADABLE: 'No se pudo leer',
      TRANSCODING_UNAVAILABLE: 'Hay que convertirlo y ffmpeg no está disponible',
      TRANSCODING_FAILED: 'No se pudo convertir',
      WRITE_FAILED: 'No se pudo mover a la biblioteca',
    },
    actions: {
      review: 'Completar e importar',
      importAsIs: 'Importar tal cual',
      addVersion: 'Añadir como otra copia',
      retry: 'Reintentar',
      ignore: 'Ignorar',
      discard: 'Descartar',
    },
    review: {
      title: 'Completar las etiquetas',
      titleField: 'Título',
      artist: 'Artista',
      albumArtist: 'Artista del álbum',
      album: 'Álbum',
      track: 'Pista',
      disc: 'Disco',
      year: 'Año',
      genre: 'Género',
      help: 'Las etiquetas se escriben en el archivo antes de importarlo.',
      import: 'Guardar e importar',
      suggestions: {
        title: 'Sugerencias de AcoustID',
        score: '{percent} %',
        track: 'pista {track}',
        discTrack: 'disco {disc}, pista {track}',
        notFound: 'AcoustID no reconoce esta canción.',
        unidentifiable:
          'No se pudo tomar la huella de este archivo para buscarlo en AcoustID (un formato que necesita ffmpeg, un álbum en un archivo con su hoja CUE…).',
        none: 'Sin sugerencias por ahora. Con AcoustID activado en Ajustes (servicios en línea), Arcanist las busca a partir del sonido del archivo.',
      },
    },
  },
  organize: {
    open: 'Organizar',
    autoBadge: 'Automático',
    title: 'Organizar «{name}»',
    subtitle: 'Mueve los archivos de la biblioteca a carpetas y nombres según una plantilla.',
    template: 'Plantilla',
    templateHelp:
      'La / separa carpetas y la última parte nombra el archivo, que debe llevar {title}. El texto entre < y > se omite cuando falta un dato. Datos disponibles:',
    useDefault: 'Usar la plantilla por defecto',
    auto: 'Organizar automáticamente los archivos nuevos',
    autoHelp:
      'Tras cada escaneo se mueven los archivos añadidos desde que lo actives. Los que ya están solo se mueven al organizar ahora.',
    saved: 'Organización guardada.',
    preview: 'Vista previa',
    previewSummary:
      '{move} archivo(s) cambiarían de sitio, {inPlace} ya están bien y {others} archivo(s) más irían con ellos (letras, carátulas…).',
    unavailable: 'Alguna ubicación no está disponible y se ha dejado fuera.',
    nowHelp: 'Cada movimiento queda registrado para poder deshacerlo.',
    now: 'Organizar ahora',
    saveFirst: 'Guarda la plantilla antes de organizar con ella.',
    history: 'Historial',
    moved: '{moved} de {total} movidos',
    failed: '{count} con error',
    undo: 'Deshacer',
    status: {
      RUNNING: 'Organizando…',
      COMPLETED: 'Completado',
      FAILED: 'Incompleto',
      UNDOING: 'Deshaciendo…',
      UNDONE: 'Deshecho',
    },
    kind: {
      MANUAL: 'A petición',
      AUTOMATIC: 'Automático',
    },
  },
  stats: {
    libraryFilter: 'Biblioteca',
    allLibraries: 'Todas las bibliotecas',
    listeners: 'Oyentes',
    skips: 'Saltadas',
    skipRate: '{rate} % de lo que empezó',
    activeDays: 'Días con música',
    perDay: '{plays} reproducciones por día',
    added: 'Canciones nuevas',
    remotePlays: 'Desde otros servidores',
    topGenres: 'Géneros más escuchados',
    noGenres: 'Sin géneros en lo escuchado.',
    byHour: 'Por hora del día',
    byWeekday: 'Por día de la semana',
    remote: 'Remoto · {server}',
    title: 'Estadísticas',
    subtitle: 'Qué hay en las bibliotecas y cómo se escucha.',
    periods: { WEEK: 'Semana', MONTH: 'Mes', YEAR: 'Año', ALL: 'Siempre' },
    library: 'Biblioteca',
    tracks: 'Canciones',
    albums: 'Álbumes',
    artists: 'Artistas',
    files: 'Archivos',
    duration: 'Duración',
    size: 'Tamaño',
    formats: 'Formatos',
    unknownFormat: 'Desconocido',
    listening: 'Escucha',
    plays: 'Reproducciones',
    listened: 'Tiempo escuchado',
    distinctTracks: 'Canciones distintas',
    timeline: 'Reproducciones por día',
    timelineMonths: 'Reproducciones por mes',
    topTracks: 'Canciones más escuchadas',
    topArtists: 'Artistas más escuchados',
    topAlbums: 'Álbumes más escuchados',
    users: 'Usuarios',
    user: 'Usuario',
    lastPlayed: 'Última escucha',
    never: 'Nunca',
    nothing: 'Nadie ha escuchado nada en este periodo.',
    playsCount: '{count} rep.',
    hours: '{hours} h',
  },
  ai: {
    title: 'AI',
    subtitle:
      'Analiza cada canción para software de DJ y clientes con asistencia de IA, como el AI DJ de ULTIMATE: beats, secciones, energía, voz y puntos de mezcla.',
    activity: {
      DISABLED: 'Apagado',
      UNAVAILABLE: 'No disponible en esta plataforma',
      IDLE: 'En espera',
      PAUSED: 'En pausa',
      RUNNING: 'Analizando',
    },
    mode: { major: 'mayor', minor: 'menor' },
    analysis: {
      title: 'Análisis',
      description:
        'Las canciones locales se analizan en segundo plano; las de S3, Ultimate Cloud u otro Arcanist, cuando un cliente las pide.',
      on: 'Activado',
      off: 'Apagado',
      speed: '{count} canciones/min',
      analyzed: 'Analizadas',
      pending: 'Pendientes',
      failed: 'Fallidas',
      version: 'Versión del analizador',
      reanalyze: 'Reanalizar todo',
      reanalyzeConfirm: 'Pulsa otra vez para reanalizar todo',
      reanalyzing: 'Se reanalizarán todas las canciones',
    },
    performance: {
      title: 'Rendimiento',
      description:
        'Cuánto del servidor usa el análisis. Cada hilo ocupa un núcleo mientras analiza una canción.',
      threads: 'Canciones a la vez',
      pauseWhileScanning: 'Pausar mientras se escanea',
      pauseWhileStreaming: 'Pausar mientras alguien escucha',
      onDemandSources:
        'Analizar bajo pedido las canciones de S3, Ultimate Cloud y otros servidores (se descargan una vez)',
      shareServerStats:
        'Compartir con los clientes las estadísticas de escucha del servidor (todos juntos, sin nombres)',
      saved: 'Ajustes guardados',
    },
    inspector: {
      title: 'Inspector',
      description:
        'Busca una canción para ver su análisis. Haz clic en cualquier punto para oír ese momento.',
      search: 'Título, artista o álbum',
      searchButton: 'Buscar',
      legend:
        'Arriba, las secciones; en violeta, la energía, con los compases (líneas largas) y los beats; en amarillo, el momento destacado; en rosa, la voz; flechas verdes y rojas, puntos de entrada y salida de mezcla; círculos, puntos de scratch.',
      tempo: 'Tempo',
      stable: 'estable',
      varying: 'variable',
      key: 'Tonalidad',
      loudness: 'Sonoridad',
      music: 'Música',
      stale: 'Este análisis es de una versión anterior del analizador y se está rehaciendo.',
      pending: 'Pendiente: puesto {position} en la cola.',
      prioritize: 'Priorizar',
      failed: 'No se pudo analizar esta canción.',
    },
  },
  serverAudio: {
    title: 'Audio del servidor',
    subtitle:
      'El servidor reproduce por un dispositivo de audio conectado a él: altavoces, un DAC USB o HDMI.',
    output: {
      title: 'Salida',
      description:
        'Aparece en «reproducir en» de los reproductores y, con una cuenta de Ultimate Cloud vinculada, como dispositivo de Ultimate Connect.',
      on: 'Activado',
      off: 'Desactivado',
      playsOn: 'Suena por',
      noDevice: 'No hay ningún dispositivo abierto',
      noDeviceHelp:
        'Puede estar desconectado u ocupado por otro programa. Arcanist lo vuelve a intentar solo.',
      noDevices:
        'El servidor no ve ningún dispositivo de audio. En Docker o en un NAS hay que darle acceso a la tarjeta de sonido (por ejemplo, --device /dev/snd).',
      connect: 'Dispositivo de Ultimate Connect en {count} cuenta(s).',
    },
    unavailable: {
      title: 'El servidor no puede reproducir audio',
      description:
        'Falta su reproductor nativo o, en Linux, la biblioteca de sonido ALSA (libasound2).',
    },
    settings: {
      title: 'Dispositivo',
      description: 'Por dónde suena la música y con qué nombre se muestra.',
      device: 'Dispositivo de salida',
      systemDefault: 'El predeterminado del sistema',
      default: 'predeterminado',
      deviceHelp:
        'Con el predeterminado, la música sigue al dispositivo que elija el sistema. Si el elegido desaparece, se usa el predeterminado.',
      name: 'Nombre',
      nameHelp: 'Con este nombre aparece en «reproducir en» y en Ultimate Connect.',
      test: 'Probar',
    },
    playing: {
      title: 'Lo que suena',
      description:
        'Quien empieza una cola la controla mientras suena; los administradores también. Las reproducciones cuentan para quien la empezó.',
      nothing: 'No suena nada.',
      someoneElse: 'cola de otra persona',
    },
    saved: 'Audio del servidor guardado',
  },
  storage: {
    title: 'Almacenamiento',
    measuring: 'Midiendo…',
    measured: 'Medido {when}.',
    refresh: 'Volver a medir',
    details: 'Ver el detalle',
    arcanist: 'Ocupa Arcanist',
    music: 'Tu música (carpetas del servidor)',
    free: 'Libre en el disco de datos',
    ofDisk: 'de {total}',
    dataDirectory: 'Directorio de datos:',
    postgresElsewhere:
      'La base de datos es PostgreSQL: su tamaño cuenta en lo que ocupa Arcanist, aunque esté en otro servidor.',
    parts: {
      DATABASE: 'Base de datos',
      ARTWORK: 'Carátulas',
      THUMBNAILS: 'Miniaturas (se rehacen solas)',
      TRANSCODES: 'Audio convertido (caché y versiones)',
      REMOTE_CACHE: 'Música de otros servidores en caché',
      TEMPORARY: 'Archivos en tránsito',
      LOGS: 'Registros',
      TOOLS: 'Herramientas (ffmpeg…)',
      PLAYERS: 'Reproductores',
      EXTENSIONS: 'Extensiones',
      OTHER: 'Ajustes, claves y otros',
    },
    catalog: {
      title: 'Contenido',
      description: 'Lo que hay en el catálogo y los datos de los usuarios.',
      libraries: 'Bibliotecas',
      artists: 'Artistas',
      albums: 'Álbumes',
      tracks: 'Pistas',
      files: 'Archivos',
      missingFiles: 'Archivos desaparecidos',
      playlists: 'Playlists',
      users: 'Usuarios',
      plays: 'Reproducciones',
      ratings: 'Valoraciones',
    },
    libraries: {
      title: 'Música por biblioteca',
      description:
        'Lo que ocupan los archivos de cada biblioteca donde están. Las bibliotecas personales de los usuarios no aparecen.',
      name: 'Biblioteca',
      where: 'Dónde',
      files: 'Archivos',
      size: 'Tamaño',
      none: 'No hay bibliotecas.',
      kinds: {
        LOCAL: 'Este servidor',
        PEER: 'Otro Arcanist',
        ULTIMATE_CLOUD: 'Ultimate Cloud',
        S3: 'S3',
      },
    },
    tables: {
      title: 'Tablas de la base de datos',
      rows: '{rows} filas',
      all: 'Ver las {count} tablas',
      fewer: 'Ver menos',
    },
  },
  transcoding: {
    page: {
      title: 'Transcodificación',
      subtitle:
        'Convertir audio: ffmpeg, la calidad que reciben las apps de Ultimate, las conversiones guardadas y dónde se convierte la música de otros servidores.',
    },
    clients: {
      title: 'Calidad de las apps de Ultimate',
      description:
        'Lo que reproduce cada app de Ultimate cuando el usuario no elige otra calidad. Convertir ahorra datos y espacio en los dispositivos; el original suena exactamente igual que el archivo.',
      desktop: { name: 'Ultimate Desktop', help: 'En el ordenador, normalmente en tu red.' },
      web: { name: 'Ultimate Web Player', help: 'En el navegador, también desde fuera de casa.' },
      mobile: {
        name: 'Apps móviles',
        help: 'En teléfonos y tabletas, a menudo con datos móviles.',
      },
      original: 'Original (sin convertir)',
      flac: 'FLAC (sin pérdida)',
      note: 'Cada usuario puede elegir otra calidad en su app. Convertir necesita ffmpeg. HE-AAC necesita un ffmpeg compilado con libfdk_aac (ver la guía); sin él, se envía AAC.',
      heAacMissing: 'este ffmpeg envía AAC',
      saved: 'Calidad guardada',
    },
    title: 'Conversión de audio',
    description:
      'Arcanist convierte las canciones al formato y la calidad que pide cada reproductor (por ejemplo, al escuchar fuera de casa). Puede guardar lo convertido para no repetir el trabajo.',
    noFfmpeg:
      'Sin ffmpeg no se convierte nada: descárgalo o indica dónde está en la tarjeta de ffmpeg.',
    cache: {
      title: 'Caché de conversiones',
      enabled: 'Guardar las canciones convertidas',
      help: 'Una canción convertida entera se guarda; la próxima vez se envía al instante y se puede adelantar dentro de ella. Las que menos se escuchan salen primero.',
      size: 'Tamaño máximo',
      age: 'Quitar las que no se escuchan en',
      days: '{count} días',
      usage: 'Ocupa {bytes} en {count} canciones.',
      clear: 'Vaciar la caché',
      cleared: 'Caché vaciada',
    },
    pregenerate: {
      title: 'Versiones preparadas de antemano',
      help: 'Convierte en segundo plano todas las canciones a los formatos que elijas, con prioridad baja y esperando mientras alguien escucha. Luego se sirven sin convertir nada.',
      pros: 'A favor',
      prosList:
        'Empiezan al instante y se puede adelantar dentro de ellas · Escuchar no gasta CPU: ideal para un NAS o un equipo modesto · Varias personas a la vez sin cuellos de botella · Las bibliotecas en S3 no se descargan enteras para convertirlas',
      cons: 'En contra',
      consList:
        'Ocupan espacio por cada formato (abajo verás cuánto) · La primera vez tarda (horas en bibliotecas grandes) y usa la CPU · Solo sirve si los reproductores piden exactamente ese formato y calidad · Las canciones nuevas se convierten tras cada escaneo',
      formats: 'Formatos',
      libraries: 'Bibliotecas',
      noHeAac:
        'HE-AAC no está disponible: este ffmpeg no lo codifica. Necesita uno con libfdk_aac (o el de macOS, con aac_at).',
      allLibraries: 'Todas (locales y S3)',
      onlyLossless:
        'Solo de originales sin pérdida (FLAC, WAV…): los MP3 y similares ya suelen ser pequeños',
      progress: '{done} de {total} · {bytes} de unos {estimated}',
      working: 'Convirtiendo…',
      none: 'Ninguno: se convierte al escuchar.',
    },
    save: 'Guardar',
    saved: 'Ajustes de conversión guardados',
  },
  host: {
    kinds: {
      WINDOWS: 'Windows (app de la bandeja)',
      WINDOWS_SERVICE: 'Windows (servicio)',
      MAC_OS: 'macOS (Arcanist.app)',
      DOCKER: 'Docker',
      SYNOLOGY: 'Synology (paquete de DSM)',
      QNAP: 'QNAP (paquete de QTS)',
      OPEN_MEDIA_VAULT: 'OpenMediaVault (plugin)',
      TRUE_NAS: 'TrueNAS SCALE (app)',
      UNRAID: 'Unraid (plantilla de Docker)',
      ROCKSTOR: 'Rockstor (Rock-on)',
      DEBIAN_PACKAGE: 'Ubuntu o Debian (paquete .deb)',
      SYSTEMD: 'Linux (servicio de systemd)',
      OTHER: 'Otro (arrancado a mano)',
    },
    card: {
      title: 'Este servidor',
      description:
        'Dónde y cómo corre Arcanist: el panel da los pasos que corresponden cuando algo se hace fuera de Arcanist.',
      kind: 'Instalado como',
      system: 'Sistema',
      container: 'En un contenedor',
      user: 'Usuario del sistema',
      data: 'Datos',
      supervised: 'Se reinicia solo',
      yes: 'Sí',
      no: 'No',
    },
    folders: {
      title: '¿Arcanist no ve la carpeta?',
      WINDOWS:
        'Arcanist corre con tu usuario de Windows: ve las mismas carpetas que tú. Las unidades de red asignadas (Z:) no existen para él: usa la ruta de red (\\\\servidor\\carpeta).',
      WINDOWS_SERVICE:
        'El servicio corre como «NT SERVICE\\Arcanist» y solo lee las carpetas a las que tiene permiso. Dale lectura (o modificación, para subir u organizar) en la carpeta: Propiedades → Seguridad → Editar → Agregar → «NT SERVICE\\Arcanist». Para música en un NAS, usa el modo app de la bandeja o cambia la cuenta del servicio en services.msc.',
      MAC_OS:
        'macOS pide permiso para carpetas como Música, Documentos, Escritorio o los discos externos: en Ajustes del Sistema → Privacidad y seguridad → Acceso total al disco, activa Arcanist y reinícialo desde la barra de menús.',
      DOCKER:
        'Arcanist solo ve las carpetas montadas en su contenedor (por ejemplo /music). Añade la tuya con -v /ruta/en/el/equipo:/music (o en volumes: de Compose), vuelve a crear el contenedor y elígela aquí por su ruta dentro del contenedor. El usuario del contenedor ({user}) debe poder leerla.',
      SYNOLOGY:
        'Arcanist corre como el usuario interno sc-arcanist. Dale permiso en Panel de control → Carpeta compartida → la carpeta → Editar → Permisos → «Usuarios internos del sistema» → sc-arcanist: lectura para escuchar, lectura y escritura para subir u organizar. La ventana de Arcanist en DSM muestra qué carpetas puede leer.',
      QNAP: 'Arcanist corre como administrador de la NAS y lee todas las carpetas compartidas: están en /share (por ejemplo /share/Music o /share/Multimedia).',
      OPEN_MEDIA_VAULT:
        'Añade la carpeta compartida en Servicios → Arcanist → Carpetas compartidas (con «Permitir escritura» para subir u organizar) y pulsa Aplicar. La ruta que hay que elegir aquí aparece en esa lista.',
      TRUE_NAS:
        'Arcanist solo ve los almacenamientos de su app. Añade el dataset en Apps → arcanist → Editar → Almacenamiento adicional y dale al grupo apps (568) lectura en él (Datasets → Editar ACL; modificación para subir u organizar).',
      UNRAID:
        'Arcanist solo ve las rutas de su plantilla. En Docker → Arcanist → Editar, añade una ruta (Add another Path) con tu share, por ejemplo /mnt/user/music → /music, y elígela aquí por su ruta dentro del contenedor.',
      ROCKSTOR:
        'Arcanist solo ve sus comparticiones. Añade la tuya en Rock-ons → Arcanist → Add Storage y dale acceso (Access control) al dueño de la compartición de datos de Arcanist.',
      DEBIAN_PACKAGE:
        'El servicio corre con un usuario propio ({user}) y solo lee las carpetas que puede leer cualquiera. Para las demás, dale un grupo que pueda leerlas: sudo systemctl edit arcanist, añade [Service] y SupplementaryGroups=<grupo> (y ReadWritePaths=<carpeta> para subir u organizar), y luego sudo systemctl restart arcanist.',
      SYSTEMD:
        'El servicio corre con un usuario propio ({user}) y solo lee las carpetas que puede leer cualquiera. Para las demás, dale un grupo que pueda leerlas: sudo systemctl edit arcanist, añade [Service] y SupplementaryGroups=<grupo> (y ReadWritePaths=<carpeta> para subir u organizar), y luego sudo systemctl restart arcanist.',
      default:
        'Arcanist corre como el usuario {user}: la carpeta tiene que estar en este servidor y ese usuario tiene que poder leerla (y escribir en ella para subir u organizar).',
    },
    audio: {
      WINDOWS:
        'Conecta o activa una salida de audio en Windows (Configuración → Sistema → Sonido); la lista se actualiza sola.',
      WINDOWS_SERVICE:
        'Conecta o activa una salida de audio en Windows (Configuración → Sistema → Sonido); la lista se actualiza sola.',
      MAC_OS:
        'Conecta o activa una salida de audio en Ajustes del Sistema → Sonido; la lista se actualiza sola.',
      DOCKER:
        'Pásale la tarjeta de sonido al contenedor: --device /dev/snd --group-add audio (en Compose, devices: y group_add:) y vuelve a crearlo.',
      TRUE_NAS: 'Edita la app (Apps → arcanist → Editar), activa «Server Audio» y guarda.',
      UNRAID:
        'En Docker → Arcanist → Editar, añade a Extra Parameters --device /dev/snd --group-add <id del grupo audio>. Unraid puede no tener cargados los drivers de sonido.',
      ROCKSTOR:
        'Los Rock-ons no pasan la tarjeta de sonido al contenedor: el audio del servidor no está disponible en Rockstor.',
      SYNOLOGY:
        'DSM no deja que un paquete se dé acceso a la tarjeta de sonido: la ventana de Arcanist en DSM dice si hay una tarjeta y qué hace falta.',
      QNAP: 'Arcanist corre como administrador: si QTS no muestra la tarjeta o el DAC USB conectado, no hay dispositivo que usar.',
      OPEN_MEDIA_VAULT:
        'Activa «Audio del servidor» en Servicios → Arcanist → Ajustes y pulsa Aplicar.',
      DEBIAN_PACKAGE:
        'El servicio no ve los dispositivos: sudo systemctl edit arcanist, añade [Service], PrivateDevices=no y SupplementaryGroups=audio, y luego sudo systemctl restart arcanist.',
      SYSTEMD:
        'El servicio no ve los dispositivos: sudo systemctl edit arcanist, añade [Service], PrivateDevices=no y SupplementaryGroups=audio, y luego sudo systemctl restart arcanist.',
      default:
        'El usuario {user} tiene que poder abrir la tarjeta de sonido (en Linux, el grupo audio).',
    },
    restart: {
      unsupervised:
        'Este servidor no tiene quien lo vuelva a iniciar: al reiniciar se detendrá y habrá que iniciarlo a mano.',
      WINDOWS: 'La bandeja lo vuelve a iniciar. Si no vuelve, abre Arcanist desde el menú Inicio.',
      WINDOWS_SERVICE:
        'El servicio se vuelve a iniciar solo. Si no vuelve, inícialo desde la bandeja o en services.msc → Arcanist → Iniciar.',
      MAC_OS:
        'La app de la barra de menús lo vuelve a iniciar. Si no vuelve, abre Arcanist desde Aplicaciones.',
      DOCKER:
        'Docker lo vuelve a iniciar si el contenedor tiene una política de reinicio (--restart unless-stopped). Si no vuelve: docker start arcanist (o docker compose up -d).',
      SYNOLOGY:
        'El paquete lo vuelve a iniciar. Si no vuelve, ejecútalo en el Centro de paquetes → Arcanist.',
      QNAP: 'El paquete lo vuelve a iniciar. Si no vuelve, inícialo en App Center → Arcanist.',
      OPEN_MEDIA_VAULT:
        'systemd lo vuelve a iniciar. Si no vuelve, pulsa Reiniciar en Servicios → Arcanist → Ajustes.',
      TRUE_NAS: 'TrueNAS lo vuelve a iniciar. Si no vuelve, inícialo en Apps → arcanist.',
      UNRAID:
        'Docker lo vuelve a iniciar. Si no vuelve, inícialo en la pestaña Docker → Arcanist → Start.',
      ROCKSTOR: 'Docker lo vuelve a iniciar. Si no vuelve, enciéndelo en Rock-ons → Arcanist.',
      DEBIAN_PACKAGE:
        'systemd lo vuelve a iniciar. Si no vuelve: sudo systemctl start arcanist (y sudo journalctl -u arcanist para ver por qué).',
      SYSTEMD:
        'systemd lo vuelve a iniciar. Si no vuelve: sudo systemctl start arcanist (y sudo journalctl -u arcanist para ver por qué).',
      default: 'Si no vuelve, inícialo otra vez como lo iniciaste.',
    },
    dlna: {
      DOCKER:
        'DLNA se anuncia por multicast en la red local, que no sale del contenedor: usa la red del equipo (--network host, o network_mode: host en Compose).',
      TRUE_NAS:
        'DLNA se anuncia por multicast en la red local: edita la app y activa «Host Network».',
      UNRAID:
        'DLNA se anuncia por multicast en la red local: en Docker → Arcanist → Editar, elige Network Type: Host.',
      ROCKSTOR:
        'DLNA se anuncia por multicast en la red local, que no sale del contenedor de un Rock-on: los dispositivos pueden no encontrar Arcanist.',
    },
  },
  updates: {
    notice: {
      title: 'Arcanist {version} está disponible',
      current: 'Tienes la {current}. Publicada el {date}.',
      later: 'Recordarme mañana',
      open: 'Ir a la página del release',
    },
    howTo: {
      WINDOWS:
        'La app de la bandeja instala la actualización (y la del servicio, si lo usas): menú de la bandeja → Buscar actualizaciones.',
      DOCKER:
        'Baja la imagen nueva y vuelve a crear el contenedor: docker compose pull && docker compose up -d.',
      SYNOLOGY: 'Actualízalo desde el Centro de paquetes de DSM cuando esté disponible allí.',
      DEBIAN_PACKAGE:
        'Descarga el .deb de la página del release e instálalo con sudo apt install ./arcanist_….deb: el servicio se reinicia solo.',
      OPEN_MEDIA_VAULT:
        'Descarga los .deb de arcanist y openmediavault-arcanist de la página del release y súbelos en Sistema → Complementos (o instálalos con apt install ./…deb): tus datos se conservan.',
      QNAP: 'Descarga el .qpkg de tu NAS (x86_64 o arm_64) de la página del release e instálalo en App Center → Instalar manualmente: tus datos se conservan.',
      TRUE_NAS:
        'Actualízala en Apps cuando TrueNAS ofrezca la versión nueva (si la instalaste con YAML, cambia la etiqueta de la imagen o vuelve a desplegarla).',
      UNRAID: 'En la pestaña Docker, pulsa Check for updates y luego Apply update en Arcanist.',
      ROCKSTOR:
        'Desinstala el Rock-on y vuelve a instalarlo con las mismas comparticiones: baja la imagen nueva y conserva tus datos.',
      WINDOWS_SERVICE:
        'La app de la bandeja instala la actualización y actualiza el servicio: menú de la bandeja → Buscar actualizaciones.',
      MAC_OS:
        'Arcanist.app se actualiza sola: menú de la barra → Buscar actualizaciones (o con Homebrew: brew upgrade --cask arcanist).',
      SYSTEMD:
        'Descarga el tarball de la página del release, reemplaza la carpeta del servidor y reinicia: sudo systemctl restart arcanist.',
      OTHER: 'Descarga el paquete de tu sistema desde la página del release.',
    },
    card: {
      title: 'Actualizaciones',
      description:
        'Arcanist mira en GitHub cada 12 horas si hay una versión nueva y avisa a los administradores en el panel. Solo se consulta la lista de versiones: no se envía nada.',
      enabled: 'Buscar versiones nuevas',
      current: 'Versión instalada: {version}',
      available: 'Hay una nueva: {version}',
      upToDate: 'es la más reciente',
      checked: 'Comprobado {when}',
      check: 'Buscar ahora',
    },
  },
  extensions: {
    setup: {
      consent:
        'Arcanist lo descargará de sus fuentes oficiales y comprobará su firma. Puedes hacerlo más tarde desde la página de la extensión.',
      later: 'Más tarde',
      done: 'Listo.',
      failed: 'No se pudo completar.',
    },
    blocked: 'Bloqueado: no es open source',
    blockedHelp: 'El modo 100% open source está activado (Ajustes).',
    page: {
      noLibrary: 'Ninguna',
      secretKept: 'Guardado (escribe para cambiarlo)',
      emptyList: 'Nada.',
      unsaved: 'Hay cambios sin guardar.',
      unavailable:
        'Esta extensión no está en marcha o no tiene página de gestión. Actívala (y reinicia el servidor si hace falta) en Extensiones.',
    },
    descriptions: {
      ai: 'Análisis de las canciones para software de DJ y clientes con asistencia de IA (el AI DJ de ULTIMATE).',
      dlna: 'Servidor DLNA/UPnP para televisores, altavoces y apps de la red local, y «reproducir en».',
      mstream: 'La API de mStream, para las apps de mStream.',
      'server-audio':
        'Reproduce por un dispositivo de audio conectado al servidor, desde «reproducir en» y Ultimate Connect.',
      subsonic:
        'La API de Subsonic y OpenSubsonic, para apps como Symfonium, DSub, Feishin o Amperfy.',
      'ultimate-cloud':
        'Ultimate Cloud de LinkFast: sus bibliotecas y Cloud Drop, la sincronización, las bibliotecas en S3, la conexión transparente y sus servicios en línea.',
    },
    title: 'Extensiones',
    subtitle: 'Reproductores web y extensiones del servidor.',
    builtIn: 'Integrado',
    uninstall: 'Desinstalar',
    players: {
      browse: 'Ver más reproductores en el Marketplace',
      title: 'Reproductores web',
      description:
        'El predeterminado se sirve en la raíz del servidor; todos están además en /players/<id>/.',
      default: 'Predeterminado',
      makeDefault: 'Hacer predeterminado',
      none: 'No hay reproductores instalados.',
      installed: 'Reproductor instalado',
    },
    list: {
      title: 'Extensiones',
      description:
        'Añaden APIs y funciones al servidor, como la compatibilidad con apps de Subsonic y de mStream. Las apps se conectan con una contraseña de aplicación.',
      enabled: 'Activada',
      failed: 'Con errores',
      managed: 'Gestionada',
      managedHelp:
        'La instaló la configuración del servidor: siempre está activa y no se puede desinstalar desde aquí. Sí se puede actualizar instalando una versión más nueva.',
      restartRequired: 'Requiere reiniciar',
      restartRequiredHelp: 'El cambio se aplicará al reiniciar el servidor.',
      restartNeeded:
        'Hay extensiones instaladas o activadas que funcionarán cuando el servidor se reinicie.',
      restartNow: 'Reiniciar ahora',
      restartDone: 'El servidor se reinició',
      restartUnreachable:
        'El servidor no respondió. Si no tiene un supervisor (servicio, Docker, systemd o la bandeja), hay que volver a iniciarlo a mano.',
      routes: 'Rutas',
      installed: 'Extensión instalada: actívala y reinicia el servidor para usarla.',
    },
    marketplace: {
      closedSource: 'No es open source',
      closedSourceHelp:
        'El modo 100% open source está activado: desactívalo en Ajustes para instalarla.',
      kind: 'Qué buscar',
      kinds: { extensions: 'Extensiones', players: 'Reproductores web' },
      extensions: {
        title: 'Marketplace de extensiones',
        description:
          'Extensiones del catálogo de Arcanist, listas para instalar con un clic. Las oficiales son del proyecto; las de la comunidad se revisan al añadirlas al catálogo.',
        search: 'Buscar extensiones',
        none: 'No hay extensiones que coincidan.',
        createTitle: 'Crea y publica tu extensión',
        createDescription:
          'Cualquiera puede hacer una extensión para Arcanist y ofrecerla en este marketplace.',
        write: 'Escríbela con el SDK de Arcanist (C#), siguiendo la guía para desarrolladores.',
        release:
          'Publica su .zip como release de un repositorio público de GitHub, con su código fuente.',
        propose:
          'Propónla en el catálogo con un issue («Add an extension»): una automatización comprueba el paquete y el equipo la revisa antes de listarla.',
        manage:
          'Después publicas versiones nuevas, cambias su ficha o la retiras tú mismo, también con issues.',
        proposeButton: 'Proponer una extensión',
      },
      players: {
        title: 'Marketplace de reproductores web',
        description:
          'Reproductores web para escuchar desde el navegador: Ultimate Web Player y los que publique la comunidad. Uno se sirve en la dirección del servidor y todos en /players/<id>/.',
        search: 'Buscar reproductores',
        none: 'No hay reproductores que coincidan.',
        createTitle: 'Crea y publica tu reproductor web',
        createDescription:
          'Cualquiera puede hacer un reproductor web para Arcanist (con la API GraphQL o REST) y ofrecerlo en este marketplace.',
        write:
          'Hazlo con la tecnología web que prefieras, siguiendo la guía de reproductores (manifiesto, rutas y API).',
        release:
          'Publica su .zip como release de un repositorio público de GitHub (con su código, si es open source).',
        propose:
          'Propónlo en el catálogo con un issue («Add a web player»): una automatización comprueba el paquete y el equipo lo revisa antes de listarlo.',
        manage:
          'Después publicas versiones nuevas, cambias su ficha o lo retiras tú mismo, también con issues.',
        proposeButton: 'Proponer un reproductor',
      },
      playerInstalled: '{name} instalado. Puedes hacerlo el predeterminado en Reproductores.',
      refresh: 'Actualizar',
      official: 'Oficial',
      community: 'Comunidad',
      homepage: 'Más información',
      install: 'Instalar',
      update: 'Actualizar',
      installed: 'Instalada',
      needs: 'Necesita Arcanist {version}',
      categories: 'Categorías',
      pricingFilter: 'Modelo',
      anyPricing: 'Cualquiera',
      prices: 'Precios',
      pricing: {
        OPEN_SOURCE: 'Open source',
        FREE: 'Gratis',
        PAID: 'De pago',
        FREEMIUM: 'Freemium',
      },
      pricingHelp: {
        OPEN_SOURCE: 'Gratis, con su código fuente publicado bajo una licencia libre.',
        FREE: 'Gratis, sin su código fuente.',
        PAID: 'De pago: se compra a su autor, en la propia extensión o en su web.',
        FREEMIUM: 'Gratis, con funciones de pago que se compran a su autor.',
      },
      allCategories: 'Todas',
      create: {
        guide: 'Guía para desarrolladores',
        howToPublish: 'Cómo se publica',
      },
      communityTrust:
        'Esta extensión es de la comunidad, no del proyecto Arcanist. Se ejecuta dentro del servidor con todos sus permisos: puede leer y cambiar cualquier dato y archivo al que el servidor tenga acceso. Instálala solo si confías en su autor.',
    },
    tabs: {
      installed: 'Instaladas',
      marketplace: 'Marketplace',
      players: 'Reproductores',
    },
    restartDialog: {
      title: 'Reiniciando el servidor',
      stopping: 'Deteniendo el servidor…',
      starting:
        'Esperando a que el servidor vuelva a estar en marcha. Suele tardar unos segundos; no cierres esta página.',
      elapsed: '{seconds} s',
    },
    installedDialog: {
      title: '«{name}» está instalada',
      enable:
        'Para usarla hay que activarla y reiniciar el servidor. El reinicio dura unos segundos: quien esté escuchando música notará un corte.',
      restart:
        'Funcionará cuando el servidor se reinicie. El reinicio dura unos segundos: quien esté escuchando música notará un corte.',
      enableAndRestart: 'Activar y reiniciar ahora',
      later: 'Más tarde',
      ready: '«{name}» está instalada y en marcha.',
    },
    install: {
      title: 'Instalar',
      zip: 'Elegir archivo .zip',
      dropTitle: {
        extension: 'Sube el .zip de una extensión',
        frontend: 'Sube el .zip de un reproductor web',
      },
      dropHelp: 'Arrástralo aquí o elígelo con el botón. También puedes instalarlo desde una URL.',
      or: 'O desde una URL:',
      urlPlaceholder: 'https://… (URL de un .zip)',
      fromUrl: 'Instalar desde URL',
      confirmTitle: '¿Confías en este paquete?',
      playerTrust:
        'Un reproductor web se ejecuta con la sesión de quien lo abre, en el mismo sitio que este panel: puede hacer todo lo que esa persona puede hacer. Instala solo reproductores de quien confíes.',
      extensionTrust:
        'Una extensión se ejecuta dentro del servidor con todos sus permisos: puede leer y cambiar cualquier dato y archivo al que el servidor tenga acceso. Instala solo extensiones de quien confíes.',
      confirm: 'Instalar',
    },
  },
  dlna: {
    title: 'DLNA',
    subtitle:
      'Televisores, consolas, Kodi o VLC de tu red local navegan y reproducen tu música, y Arcanist puede reproducir en altavoces y televisores DLNA.',
    saved: 'Ajustes de DLNA guardados',
    server: {
      title: 'Servidor DLNA',
      description:
        'DLNA no tiene inicio de sesión: cualquier dispositivo de la red local ve lo que publiques aquí. Nunca se ofrece fuera de la red local.',
      on: 'Activado: los dispositivos de la red lo ven',
      off: 'Desactivado',
      noNetwork: 'No se encontró ninguna red local en este servidor.',
      noFfmpeg:
        'Sin ffmpeg solo se envían los archivos originales: algunos dispositivos no reproducirán ciertos formatos.',
      name: 'Nombre que ven los dispositivos',
      recordPlays: 'Registrar las reproducciones en las estadísticas',
      playsPaused: 'Estadísticas en pausa: las reproducciones no cuentan',
      recordPlaysHint:
        'Lo que suena con «Reproducir en» cuenta en las estadísticas de quien lo pone. Pausa el registro mientras otro programa lea Arcanist por DLNA, por ejemplo durante una migración: los dispositivos siguen reproduciendo.',
    },
    settings: {
      title: 'Lo que ven los dispositivos',
      description:
        'Su nombre, las bibliotecas que publica y las carpetas de su menú. Se guardan juntos.',
    },
    libraries: {
      title: 'Bibliotecas publicadas',
      description: 'Solo estas bibliotecas aparecen en los dispositivos de la red.',
      none: 'No hay bibliotecas.',
    },
    sections: {
      title: 'Carpetas del menú',
      description: 'Lo que ven los dispositivos al entrar en Arcanist, en este orden.',
      up: 'Subir',
      down: 'Bajar',
      onlyFiveStars: '(solo con el modo de cinco estrellas)',
      ratingsUser: 'StarTracks y valoraciones de',
      ratingsUserHelp:
        'DLNA no tiene inicio de sesión: StarTracks y Valoradas muestran las de este usuario.',
      names: {
        ARTISTS: 'Artistas',
        ALBUMS: 'Álbumes',
        GENRES: 'Géneros',
        DECADES: 'Décadas',
        FOLDERS: 'Carpetas',
        LIBRARIES: 'Bibliotecas, cada una por separado',
        MUSIC_VIDEOS: 'Videoclips',
        PLAYLISTS: 'Playlists públicas',
        RECENTLY_ADDED: 'Añadido recientemente',
        MIXES: 'Mixes por género y década',
        STARRED: 'StarTracks',
        RATED: 'Valoradas, por estrellas',
      },
    },
    starTracks: {
      title: 'StarTracks por usuario',
      description:
        'Los StarTracks de cada usuario elegido aparecen como una carpeta dentro de StarTracks (solo canciones de las bibliotecas publicadas, para cualquiera de la red local). Cada usuario también puede mostrar u ocultar los suyos en Mi cuenta. Sin ninguno, StarTracks muestra los del usuario de abajo.',
    },
    renderers: {
      title: 'Reproducir en',
      description:
        'Altavoces y televisores de la red que reproducen lo que Arcanist les envía, en el mejor formato que admitan (sin pérdida primero).',
      off: 'Activa DLNA para encontrar dispositivos.',
      none: 'No se ha encontrado ninguno todavía.',
      refresh: 'Actualizar',
      addUrl: 'Añadir por la URL de su descripción',
      volume: 'Volumen',
      playAlbum: 'Reproducir un álbum',
      search: 'Buscar',
    },
    states: {
      STOPPED: 'Detenido',
      PLAYING: 'Reproduciendo',
      PAUSED: 'En pausa',
      TRANSITIONING: 'Cargando',
      NO_MEDIA: 'Sin nada',
    },
    actions: {
      PLAY: 'Reproducir',
      PAUSE: 'Pausa',
      STOP: 'Detener',
      NEXT: 'Siguiente',
      PREVIOUS: 'Anterior',
    },
  },
  s3: {
    open: 'Biblioteca en S3',
    title: 'Biblioteca en S3',
    subtitle:
      'Música guardada en un bucket de cualquier servicio compatible con S3 (AWS, Backblaze B2, Wasabi, Cloudflare R2, MinIO…). Se lee al escanear y al reproducir, sin descargarla entera.',
    needsPlan:
      'Las bibliotecas en S3 necesitan el plan Cloud de la cuenta de Ultimate Cloud vinculada.',
    endpoint: 'Dirección del servicio',
    endpointHelp: 'La URL S3 de tu proveedor (para AWS, https://s3.amazonaws.com).',
    bucket: 'Bucket',
    prefix: 'Carpeta',
    prefixPlaceholder: 'todo el bucket',
    region: 'Región',
    accessKeyId: 'Clave de acceso',
    secretAccessKey: 'Clave secreta',
    encrypt: 'Cifrar con una clave tuya',
    encryptHelp:
      'Arcanist cifra la música y sus nombres antes de subirlos: el servicio de S3 no puede leer nada. Sirve para cualquier biblioteca nueva en un bucket o carpeta vacíos.',
    key: 'Clave de cifrado',
    newKey: 'Generar otra',
    keyWarning:
      'Guárdala en un sitio seguro (un gestor de contraseñas). Sin ella nadie puede leer esta biblioteca, ni siquiera tú: si se pierde, la música se pierde. Con ella puedes descifrarla en cualquier otro servidor Arcanist.',
    keySaved: 'He guardado la clave',
    keyCopied: 'Clave copiada',
    pathStyle: 'Bucket en la ruta',
    pathStyleHelp: 'Para MinIO y la mayoría de servicios compatibles. Desactívalo para AWS.',
    notes:
      'La clave secreta se guarda cifrada en este servidor y no se vuelve a mostrar. Con permiso de escritura, organizar mueve los archivos dentro del bucket (nunca sobrescribe; lo quitado va a .arcanist-trash).',
    found: 'Conectado: {count} archivos de audio',
    foundMore: 'Conectado: {count} archivos de audio entre los primeros mil objetos',
    create: 'Crear biblioteca',
    created: 'Biblioteca creada; se está leyendo su contenido',
  },
  cloudSync: {
    account: 'Cuenta',
    serverAccount: 'Ultimate Cloud de {owner} (del servidor)',
    ownAccount: 'Mi Ultimate Cloud ({name})',
    title: 'Ultimate Cloud',
    needsPlan: 'Sincronizar con Ultimate Cloud necesita el plan Cloud de la cuenta vinculada.',
    downloadTo: 'Bajar a',
    mode: {
      OFF: 'No sincronizar',
      UPLOAD: 'Subir',
      DOWNLOAD: 'Bajar',
      BOTH: 'Subir y bajar',
    },
    phase: {
      listing: 'Leyendo Ultimate Cloud…',
      matching: 'Comparando…',
      uploading: 'Subiendo…',
      downloading: 'Bajando…',
    },
    queued: 'Sincronización en cola',
    lastRun: 'Sincronizada {when}',
    counts:
      '{matched} en ambos lados · {uploaded} subidas · {downloaded} bajadas · {localOnly} solo aquí',
    failures: '{count} con errores (se reintentan en cada sincronización)',
    cannotStrip: 'no se puede subir tal cual (quitar sus etiquetas cambia el audio)',
    toolError: 'la herramienta de audio no pudo leerla',
    nothingDeleted: 'Nunca se borra nada en ningún lado. Se sincroniza cada 6 h.',
    now: 'Sincronizar ahora',
  },
  cloud: {
    title: 'Ultimate Cloud',
    subtitle:
      'Vincula este servidor a tu cuenta de Ultimate Cloud para usar tu música en la nube y las funciones de pago.',
    linked: 'Servidor vinculado a Ultimate Cloud',
    unlinked: 'Cuenta desvinculada',
    account: {
      title: 'Cuenta',
      description:
        'Arcanist nunca ve tu contraseña: apruebas el acceso en el sitio de Ultimate Cloud con un código.',
      notConfigured:
        'Este servidor no tiene configurada la dirección de Ultimate Cloud (Arcanist:UltimateCloud:ApiUrl).',
      notes: {
        free: 'Con una cuenta gratuita, tu música de Ultimate Cloud aparece como fuente de biblioteca.',
        paid: 'Con el plan Cloud: sincronización, bibliotecas en S3 y acceso remoto sin emparejar.',
        local: 'Todo lo local sigue funcionando igual, con o sin cuenta.',
      },
      link: 'Vincular cuenta',
      relink: 'Vincular de nuevo',
      refresh: 'Comprobar el plan',
      unlink: 'Desvincular',
      unlinkTitle: '¿Desvincular la cuenta?',
      unlinkHelp:
        'Se apagan las funciones de Ultimate Cloud. Tus bibliotecas locales, usuarios y datos no cambian.',
      name: 'Cuenta',
      linkedAt: 'Vinculada',
    },
    pending: {
      instructions:
        'Escanea el código o abre el enlace, inicia sesión en Ultimate Cloud y confirma este código:',
      qr: 'Código QR para aprobar el acceso',
      open: 'Abrir Ultimate Cloud',
      code: 'Código',
      waiting: 'Esperando la aprobación (caduca {when})…',
    },
    plan: {
      name: 'Plan',
      storage: 'Almacenamiento',
      used: '{used} de {limit}',
      readOnly: 'solo lectura',
      renews: 'Se renueva',
      checked: 'Comprobado',
      status: {
        PastDue: 'Pago pendiente',
        PendingCancellation: 'Se cancela al final del periodo',
      },
    },
    libraries: {
      title: 'Ultimate Cloud como biblioteca',
      description:
        'La música de tu cuenta aparece como una biblioteca de este servidor, sin descargarla: se trae al reproducirla. Al principio solo la ven los administradores; dásela a quien quieras en Usuarios.',
      add: 'Añadir',
      added: 'Biblioteca añadida; se está leyendo su contenido',
      open: 'Ver «{name}»',
      LIBRARY: {
        name: 'Mi biblioteca',
        help: 'Lo que subes desde Ultimate Desktop y el reproductor web.',
        libraryName: 'Ultimate Cloud de {owner}',
      },
      CLOUD_DROP: {
        name: 'Cloud Drop',
        help: 'La música que dejas en Cloud Drop, aparte de tu biblioteca.',
        libraryName: 'Cloud Drop de {owner}',
      },
    },
    tool: {
      title: 'Sincronización',
      installed: 'Activada: ultimate-audio {version}, firmada por Ultimate',
      off: 'Desactivada',
      help: 'El componente de sincronización (ultimate-audio, de código cerrado) sube y baja música compatible con las apps de Ultimate: sincronizar bibliotecas, Cloud Drop y «Subir a mi Ultimate Cloud» lo necesitan, y se activa solo al usarlas. Con el plan Cloud, el servidor pasa a ser ULTIMATE.',
    },
    extension: {
      enabled: 'Ultimate Cloud',
      onHelp:
        'Todo lo que funciona con Ultimate Cloud: sus bibliotecas y Cloud Drop, la sincronización, las bibliotecas en S3, la conexión transparente y sus servicios en línea. Desactívalo si no piensas usar Ultimate Cloud.',
      offHelp:
        'Desactivado: este servidor nunca contacta con Ultimate Cloud, sus bibliotecas se ocultan y las funciones de su plan se apagan. Al activarlo de nuevo vuelve todo, con la cuenta todavía vinculada.',
      off: 'Desactivar',
      offTitle: '¿Desactivar Ultimate Cloud?',
      offConfirm:
        'Se ocultará todo lo referente a Ultimate Cloud: su página, Cloud Drop, la sincronización, las bibliotecas en S3 y sus servicios en línea. Las bibliotecas en Ultimate Cloud o S3 quedan desactivadas y solo se pueden eliminar. Para recuperarlo tendrás que activar la extensión de nuevo en Extensiones; la cuenta sigue vinculada.',
      switchedOff: 'Ultimate Cloud está desactivado. Actívalo de nuevo aquí, en Extensiones.',
    },
    services: {
      title: 'Servicios en línea',
      description:
        'Lo que comparte la cuenta vinculada. Solo participan las canciones identificadas con AcoustID. Las estadísticas anónimas del servidor están en Ajustes.',
      personalPlays: {
        name: 'Mis reproducciones',
        help: 'Las reproducciones del administrador que vinculó la cuenta cuentan como suyas: sus estadísticas y los rankings.',
      },
      albumPlaythroughs: {
        name: 'Álbumes escuchados enteros',
        help: 'Un álbum escuchado de la primera canción a la última, en orden, cuenta en los rankings de álbumes.',
      },
      starTracks: {
        name: 'StarTracks',
        help: 'Tus StarTracks aquí y en las apps Ultimate son los mismos: añadir o quitar uno en un lado lo hace en el otro.',
      },
      metrics: {
        name: 'Mis estadísticas',
        help: 'Muestra abajo lo que escuchas según Ultimate Cloud, desde todas las apps Ultimate y este servidor.',
      },
    },
    metrics: {
      title: 'Lo que escuchas en Ultimate',
      totals: '{plays} reproducciones, {streams} de ellas en streaming desde Ultimate Cloud',
      ThisMonth: 'Este mes',
      ThisYear: 'Este año',
      AllTime: 'Siempre',
      kind: {
        Song: 'Canción',
        Artist: 'Artista',
        Album: 'Álbum',
        Genre: 'Género',
      },
      entry: '{name} · {plays}',
      entryBy: '{name} — {by} · {plays}',
      decade: 'Tu década: {start}–{end} ({plays} reproducciones)',
    },
    tier: {
      NONE: 'Sin cuenta',
      FREE: 'Gratuito',
      PAID: 'De pago',
    },
    capabilities: {
      title: 'Funciones',
      description: 'Lo que tu plan activa en este servidor.',
      on: 'Activa',
      off: 'Inactiva',
      ULTIMATE_CLOUD_SOURCE: {
        name: 'Ultimate Cloud como biblioteca',
        help: 'Escucha desde aquí la música que guardas en Ultimate Cloud. Cuenta gratuita.',
      },
      CLOUD_SYNC: {
        name: 'Sincronización',
        help: 'Sube o baja bibliotecas entre este servidor y Ultimate Cloud. Plan Cloud.',
      },
      THIRD_PARTY_STORAGE: {
        name: 'Bibliotecas en S3',
        help: 'Bibliotecas en almacenamiento compatible con S3. Plan Cloud.',
      },
      CLOUD_CONNECT: {
        name: 'Conexión transparente',
        help: 'Tus dispositivos encuentran este servidor por la red P2P sin emparejarlos con un QR. Con cualquier cuenta.',
      },
    },
  },
  database: {
    title: 'Base de datos',
    subtitle:
      'Dónde guarda Arcanist su catálogo, usuarios y ajustes, y cómo pasar de SQLite a PostgreSQL.',
    current: {
      title: 'Base de datos actual',
      provider: 'Tipo',
      locked: 'Configurada por el entorno',
      location: 'Ubicación',
      size: 'Tamaño',
      migrated: 'Migrada',
    },
    migrate: {
      title: 'Migrar a PostgreSQL',
      description:
        'Copia todo (bibliotecas, usuarios, valoraciones, playlists, ajustes…) a PostgreSQL y el servidor pasa a usarlo, sin reiniciar.',
      connectionHelp:
        'De una base de datos vacía (o que aún no existe: se crea). El usuario necesita permiso para crear tablas.',
      locked:
        'La base de datos está fijada por la configuración del servidor (por ejemplo, variables de entorno de Docker): cámbiala allí.',
      notes: {
        maintenance:
          'Mientras dure, el servidor está en mantenimiento: no se puede escuchar música ni usar la API (unos minutos en bibliotecas grandes).',
        empty: 'Antes deben terminar los escaneos y organizaciones en curso.',
        safe: 'SQLite no se modifica: si algo falla, el servidor sigue con SQLite como si nada.',
      },
      start: 'Migrar ahora',
      confirmTitle: '¿Migrar a PostgreSQL?',
      confirmHelp:
        'El servidor entrará en mantenimiento hasta que termine. Al acabar usará PostgreSQL y te preguntaremos qué hacer con el archivo de SQLite.',
      progressTitle: 'Migración a PostgreSQL',
      maintenance:
        'El servidor está en mantenimiento. No cierres esta página si quieres ver el resultado.',
      failed: 'La migración no se completó: el servidor sigue con SQLite.',
      done: 'El servidor ya usa PostgreSQL',
      copied: '{rows} filas de {tables} tablas copiadas {when}',
      phase: {
        pausing: 'Esperando a que termine lo que está en curso…',
        schema: 'Creando las tablas en PostgreSQL…',
        copying: 'Copiando los datos…',
        verifying: 'Comprobando la copia…',
        switching: 'Cambiando a PostgreSQL…',
      },
    },
    previous: {
      title: 'El archivo de SQLite anterior',
      description:
        'El servidor ya no lo usa. Puedes conservarlo como copia de seguridad, llevarlo a otra carpeta o borrarlo.',
      file: 'Archivo',
      gone: 'Ya no está en esa ubicación',
      folder: 'Carpeta de destino',
      KEEP: {
        name: 'Dejarlo donde está',
        help: 'No se toca. Podrás borrarlo a mano cuando quieras.',
      },
      MOVE: {
        name: 'Moverlo a otra carpeta',
        help: 'Por ejemplo, a la de tus copias de seguridad. Nunca se sobrescribe otro archivo.',
      },
      DELETE: { name: 'Borrarlo', help: 'Se elimina para siempre. Tus datos están en PostgreSQL.' },
      confirm: { KEEP: 'Dejarlo', MOVE: 'Moverlo', DELETE: 'Borrarlo' },
      done: {
        KEEP: 'El archivo se queda donde está',
        MOVE: 'Archivo movido',
        DELETE: 'Archivo borrado',
      },
    },
  },
  remoteLibraries: {
    transcodingMoved: 'Dónde se convierte esta música se elige en',
    title: 'Escuchar música de otros servidores',
    description: 'Cómo se escucha aquí la música que comparten otros servidores Arcanist.',
    cache: 'Guardar en caché lo que se escucha',
    cacheHelp:
      'Si el otro servidor lo permite, cada canción que se escucha se guarda entera: la próxima vez suena al instante, se puede adelantar y no depende del otro servidor ni de la red. Nunca es una descarga.',
    size: 'Tamaño máximo',
    age: 'Quitar lo que no se escucha en',
    usage: 'Ocupa {bytes} en {count} canciones.',
    clear: 'Vaciar la caché',
    cleared: 'Caché vaciada',
    transcoding: 'Dónde se convierten',
    transcodingHelp:
      'Para escuchar en otro formato o con menos calidad. Si el otro servidor limita la calidad, convierte siempre él.',
    modes: {
      AUTO: 'Automático: aquí si está en la caché; si no, el otro servidor (y aquí si él no puede)',
      HERE: 'Siempre aquí: el otro servidor solo envía el original',
      OWNER: 'Siempre en el servidor que comparte',
    },
    save: 'Guardar',
    saved: 'Ajustes guardados',
  },
  network: {
    tabs: {
      node: 'Nodo y ajustes',
      servers: 'Servidores',
      devices: 'Dispositivos',
      activity: 'Actividad',
    },
    title: 'Red P2P',
    subtitle:
      'Llega a tu servidor desde cualquier sitio sin abrir puertos, y comparte bibliotecas con otros servidores Arcanist. El tráfico va cifrado de extremo a extremo.',
    saved: 'Cambios guardados',
    noLimit: 'Sin límite',
    online: 'Conectado',
    revoked: 'Revocado',
    revoke: 'Revocar',
    revokeTitle: '¿Revocar el acceso de {name}?',
    revokeHelp:
      'Deja de poder conectarse en el acto. Para volver, necesitará una invitación nueva.',
    edit: 'Editar {name}',
    until: 'hasta el {when}',
    accessUntil: 'Acceso hasta (opcional)',
    accessUntilHelp: 'Vacío para que no caduque. Puedes revocarlo cuando quieras.',
    lastSeen: 'visto {when}',
    lastContact: 'Último contacto {when}',
    stats:
      '{sent} enviados · media {average}/s · pico {peak}/s · {streams} reproducciones ({active} ahora)',
    node: {
      title: 'Nodo P2P',
      description:
        'Tu servidor en la red P2P. Nada es accesible hasta que emparejas un dispositivo o compartes una biblioteca.',
      enabled: 'Activada',
      running: 'En marcha',
      stopped: 'Detenida',
      relayConnected: 'Relay conectado',
      relayPending: 'Buscando relay…',
      id: 'Id del nodo',
      homeRelay: 'Relay',
      now: 'Ahora',
      traffic: '{peers} conectados · {streams} reproducciones · {rate}/s (pico {peak}/s)',
      name: 'Nombre que ven los demás',
      bandwidth: 'Ancho de banda total (kbit/s)',
      maxStreams: 'Reproducciones simultáneas en total',
    },
    relay: {
      title: 'Cómo conectar cuando no hay conexión directa',
      url: 'Dirección del relay',
      N0: {
        name: 'Relays públicos de n0 (recomendado)',
        help: 'Funciona detrás de cualquier router. El tráfico sigue cifrado de extremo a extremo: el relay solo ve que dos nodos se conectan.',
      },
      CUSTOM: {
        name: 'Mi propio relay',
        help: 'Un iroh-relay que administras tú.',
      },
      DIRECT: {
        name: 'Solo conexión directa',
        help: 'Sin relay. En algunas redes no conectará.',
      },
    },
    fromUltimate: 'Ultimate Cloud',
    fromUltimateHelp:
      'Un dispositivo de tu cuenta de Ultimate Cloud: conecta sin emparejar y deja de hacerlo si sale de la cuenta.',
    rules: {
      allowCache: 'Permitir que guarde en caché',
      allowCacheHelp:
        'Ese servidor puede guardar lo que escucha para volver a escucharlo, y adelantar dentro, sin pedírtelo. Nunca como descarga ni como copia en sus bibliotecas.',
      countPlays: 'Contar sus reproducciones',
      countPlaysHelp:
        'Sus usuarios cuentan en las estadísticas de este servidor, con su nombre allí y marcados como remotos.',
      countsPlays: 'cuenta las reproducciones',
      allowDownload: 'Permitir descargas',
      allowDownloadHelp:
        'Descargar los originales y copiarlos a sus propias bibliotecas. Si no, solo puede escuchar.',
      compressedOnly: 'Solo audio comprimido',
      compressedOnlyHelp:
        'Siempre convertido a un bitrate máximo (necesita ffmpeg en este servidor).',
      maxBitRate: 'Bitrate máximo',
      bandwidth: 'Ancho de banda (kbit/s)',
      maxStreams: 'Reproducciones simultáneas',
      downloads: 'Con descargas',
      streamOnly: 'Solo streaming',
      upTo: 'hasta {rate} kbit/s',
      bandwidthOf: '{rate} kbit/s',
      streamsAtOnce: '{count} a la vez',
    },
    servers: {
      all: 'Todo',
      allHelp:
        'Todas las bibliotecas de este servidor, también las que crees después (no las personales ni las que te comparten).',
      title: 'Compartir con otros servidores',
      description:
        'Otro servidor Arcanist recibe las bibliotecas que elijas como «Biblioteca de…», y su gente las escucha desde aquí.',
      invite: 'Invitar a un servidor',
      inviteHelp:
        'Crea un enlace que el administrador del otro servidor pega en su panel (Red P2P → Aceptar una invitación). Vale 7 días y un solo uso.',
      name: 'Nombre',
      namePlaceholder: 'Casa de Ana',
      choose: 'Bibliotecas que compartes',
      create: 'Crear invitación',
      libraries: '{count} biblioteca(s): {names}',
      empty: 'Aún no compartes con ningún servidor.',
    },
    devices: {
      title: 'Dispositivos',
      description:
        'Los dispositivos que cada usuario emparejó para llegar al servidor desde fuera. Después inician sesión como siempre.',
      empty: 'Ningún dispositivo emparejado.',
    },
    received: {
      rename: 'Renombrar',
      renameHelp:
        'El nombre con que se ve aquí la música que te comparte {server}. En el otro servidor no cambia nada.',
      sharePlays: 'Enviar las reproducciones a {name}, con el nombre de cada usuario',
      title: 'Bibliotecas que te comparten',
      description:
        'Bibliotecas de otros servidores. Aparecen en Bibliotecas, al principio solo para administradores: dales acceso a quien quieras.',
      accept: 'Aceptar una invitación',
      acceptHelp: 'Pega el enlace que te envió el administrador del otro servidor.',
      accepted: 'Invitación aceptada: leyendo su catálogo',
      library: 'Biblioteca «{library}»',
      revoked: 'Revocada',
      empty: 'Nadie te comparte bibliotecas todavía.',
      copyButton: 'Copiar a una biblioteca',
      copyHelp:
        'Trae los archivos originales de {name} a una carpeta de tus bibliotecas, dentro de una carpeta con el nombre de cada biblioteca compartida. Nada se sobrescribe.',
      copyTarget: 'Carpeta de destino',
      copyStart: 'Copiar',
      copyStarted: 'Copia iniciada',
      copy: {
        QUEUED: 'Copia en cola',
        RUNNING: 'Copiando: {copied} de {total} ({skipped} ya estaban)',
        COMPLETED: 'Copia terminada: {copied} copiados, {skipped} ya estaban',
        FAILED: 'La copia falló tras {copied} de {total}',
      },
    },
    activity: {
      description: 'Lo que pasó en la red P2P, lo más reciente primero.',
      search: 'Buscar por nombre o detalle',
      allPeers: 'Todos los nodos',
      allKinds: 'Todo tipo',
      perPage: '{count} por página',
      range: '{from}–{to} de {total}',
      previous: 'Anterior',
      next: 'Siguiente',
      page: 'Página {page} de {pages}',
      kinds: {
        JOINED: 'Uniones',
        CONNECTED: 'Conexiones',
        STREAMED: 'Reproducciones',
        DOWNLOADED: 'Descargas',
        REFUSED: 'Rechazos',
        CHANGED: 'Cambios de reglas',
        REVOKED: 'Revocaciones',
      },
      title: 'Actividad',
      empty: 'Nada todavía.',
      JOINED: '{peer} se unió',
      CONNECTED: '{peer} se conectó',
      STREAMED: '{peer} reprodujo',
      DOWNLOADED: '{peer} descargó',
      REFUSED: '{peer} fue rechazado',
      CHANGED: 'Se cambiaron las reglas de {peer}',
      REVOKED: 'Se revocó a {peer}',
    },
  },
  wishes: {
    wantsVideo: 'También quiero el videoclip',
    videoArrived: 'Videoclip: ya está',
    videoMissing: 'Videoclip: todavía no',
    target: 'Si se sube, guardarla en',
    targetAutomatic: 'Automático (según cada subida)',
    targetAutomaticNamed: 'Automático ({library})',
    targetHelp:
      'Cuando la subas tú, un reproductor o una extensión para este deseo. Si llega a cualquier biblioteca que puedas escuchar, también se cumple.',
    goesTo: 'irá a {library}',
    foundTitle: 'Ya la tienes',
    foundHelp: 'Esta canción ya está en una biblioteca que puedes escuchar:',
    where: 'Dónde',
    duration: 'Duración',
    quality: 'Calidad',
    recognisedBy: 'Reconocida por',
    tabs: {
      mine: 'Mi lista',
      all: 'De todos',
    },
    allHelp:
      'Las canciones que quieren todos los usuarios del servidor. Solo cada uno puede cambiar su lista.',
    by_user: 'Lo quiere {name}',
    link: 'Desde un enlace',
    linkHelp:
      'Pega el enlace de una canción de Spotify, YouTube Music, Deezer, Apple Music, Tidal, SoundCloud, Bandcamp, MusicBrainz… y se rellena sola, con su carátula. O escríbela a mano debajo.',
    linkPlaceholder: 'https://open.spotify.com/track/…',
    read: 'Leer enlace',
    reading: 'Leyendo…',
    readFrom: 'Leída de {service}: revisa los datos y añádela.',
    clearLink: 'Quitar el enlace',
    manual: 'O a mano',
    openIn: 'Abrir en {service}',
    otherPage: 'la página',
    cover: 'Carátula',
    title: 'Lista de deseos',
    description:
      'Las canciones que quieres y aún no tienes. Cuando una entra en una biblioteca que puedes escuchar (al escanear, subir, importar o sincronizar), se reconoce sola y te avisamos.',
    add: 'Añadir una canción',
    songTitle: 'Título',
    artist: 'Artista',
    album: 'Álbum',
    note: 'Nota',
    optional: 'Opcional',
    ids: 'Identificadores (opcional): MusicBrainz o AcoustID, para reconocerla con exactitud',
    mbid: 'Grabación de MusicBrainz',
    addButton: 'Añadir a la lista',
    added: 'Añadida a tu lista de deseos',
    alreadyThere: 'Ya está en tu biblioteca',
    wanted: 'La busco',
    wantedHelp:
      'Se reconocen por título y artista (y álbum, si lo pones), o por sus identificadores.',
    since: 'En la lista desde {when}',
    remove: 'Quitar',
    noneWanted: 'No buscas ninguna canción ahora.',
    obtained: 'Llegaron',
    obtainedHelp: 'El historial de las que ya tienes.',
    new: 'Nueva',
    wasThere: 'Ya estaba en la biblioteca',
    arrived: 'Llegó {when}',
    gone: 'ya no está en el catálogo',
    noneObtained: 'Todavía no llegó ninguna.',
    arrivedToast: '«{title}» llegó a tu biblioteca',
    by: {
      MUSIC_BRAINZ: 'por MusicBrainz',
      ACOUST_ID: 'por AcoustID',
      TAGS: 'por título y artista',
      CONTENT_HASH: 'por su audio',
    },
  },
  devices: {
    title: 'Mis dispositivos',
    description:
      'Empareja tu móvil o tu ordenador con este servidor para escuchar tu música desde cualquier sitio, sin abrir puertos (con una app compatible, como Ultimate).',
    name: 'Nombre del dispositivo (opcional)',
    namePlaceholder: 'Mi móvil',
    pair: 'Emparejar un dispositivo',
    pairHelp: 'Crea un código QR que la app escanea. Vale 30 minutos y un solo uso.',
    create: 'Crear código',
    paired: 'Emparejado {when}',
    revoked: 'Dispositivo revocado',
    none: 'No tienes dispositivos emparejados.',
  },
  invitation: {
    title: 'Invitación creada',
    qr: 'Código QR de la invitación',
    link: 'Enlace de la invitación',
    copy: 'Copiar',
    copied: 'Enlace copiado',
    copyFailed: 'No se pudo copiar: selecciónalo y cópialo a mano.',
    expires: 'Caduca {when}',
    deviceHelp:
      'Escanéalo con la app en el dispositivo. Después inicia sesión en la app como siempre.',
    serverHelp: 'Envía este enlace al administrador del otro servidor.',
  },
  arcanistImport: {
    title: 'Migrar desde otro Arcanist',
    subtitle:
      'Trae otro servidor Arcanist a este: sus bibliotecas y su música, sus usuarios y sus datos, sus ajustes, extensiones y reproductores. El otro servidor no se modifica y leerlo nunca cuenta como reproducciones.',
    how: {
      found: 'Buscar en la red',
      address: 'Por su dirección',
      link: 'Con su enlace P2P',
    },
    servers: {
      title: 'Servidores Arcanist',
      search: 'Buscar de nuevo',
      searching: 'Buscando en la red…',
      none: 'No se encontró ninguno. Escribe su dirección o usa su enlace P2P.',
      paired: 'Emparejado por la red P2P',
    },
    address: 'Dirección del otro servidor',
    addressHelp: 'Por ejemplo 192.168.1.20:8484 o https://musica.ejemplo.com.',
    link: 'Enlace de migración P2P',
    linkHelp:
      'Lleva el código dentro y llega sin abrir puertos. Al usarlo se enciende la red P2P de este servidor.',
    code: 'Código de migración',
    codeHelp:
      'Créalo en el otro servidor: Importación → «Migrar este servidor a otro». Vale 24 horas.',
    read: 'Leer el servidor',
    change: 'Cambiar',
    libraries: {
      title: 'Bibliotecas',
      help: 'Elige las que vienen y a qué biblioteca de este servidor va cada una. Varias pueden ir a la misma.',
      helpSetup:
        'Elige las que vienen y la carpeta de este servidor donde se copia cada una. Las que compartan carpeta serán una sola biblioteca.',
      same: 'Todas al mismo destino',
      sameHelp: 'Todas las bibliotecas elegidas van aquí:',
      files: '{count} archivos',
      new: 'Una biblioteca nueva',
      name: 'Nombre de la biblioteca',
      folder: 'Carpeta de este servidor',
      none: 'El otro servidor no tiene bibliotecas que se puedan traer.',
      copyNote:
        'Los archivos se copian tal cual, con sus carpetas. Nunca se sobrescribe nada: lo que ya está se salta.',
    },
    users: {
      title: 'Usuarios',
      help: 'Los que ya existen aquí (por nombre) no cambian: solo reciben sus datos.',
      all: 'Todos',
      none: 'Ninguno',
      isAdmin: 'Eres tú',
      exists: 'Ya existe',
      disabled: 'Desactivado',
      noPassword: 'Sin contraseña',
      secondFactor: 'Segundo factor',
      empty: 'El otro servidor no tiene usuarios.',
    },
    options: {
      title: 'Qué más viene',
      copyPasswords: 'Copiar las contraseñas',
      copyPasswordsHelp:
        'Los usuarios nuevos entran con la misma contraseña. Nunca se copian contraseñas de aplicación, sesiones, dispositivos ni segundos factores: quien tenía uno lo empareja de nuevo al entrar.',
      permissions: 'Permisos y acceso a bibliotecas',
      permissionsHelp:
        'Cada usuario nuevo conserva su rol, sus permisos y sus bibliotecas. Sin esto, son usuarios que pueden descargar y ven las bibliotecas visibles para todos.',
      userData: 'Datos de los usuarios',
      userDataHelp:
        'Valoraciones, reproducciones e historial, playlists y listas de deseos. Traerlos dos veces no cuenta nada dos veces.',
      settings: 'Ajustes del servidor',
      settingsHelp:
        'Valoraciones, privacidad, servicios en línea, idioma, DLNA, mStream, transcodificación, IA… No los de esta máquina (base de datos, carpetas, P2P, Ultimate Cloud).',
      extensions: 'Extensiones y reproductores',
      extensionsHelp:
        'Las integradas quedan encendidas o apagadas como allí; las demás se instalan (del marketplace si las tiene) y su reproductor predeterminado también lo será aquí.',
      installed: 'Ya está aquí',
      available: 'Se instalará',
      unavailable: 'No se puede traer',
    },
    start: 'Empezar la migración',
    startedToast: 'La migración empezó: sigue en segundo plano.',
    status: {
      QUEUED: 'En cola',
      PREPARING: 'Ajustes y extensiones',
      COPYING: 'Copiando música',
      WAITING: 'Escaneando',
      APPLYING: 'Trayendo los datos',
      COMPLETED: 'Terminada',
      FAILED: 'Falló',
      CANCELLED: 'Cancelada',
    },
    job: {
      into: 'En {libraries}',
      prepared:
        '{created} usuario(s) creados y {mapped} existentes · {libraries} biblioteca(s) nuevas · {extensions} extensión(es) y {players} reproductor(es) instalados',
      settings: 'ajustes aplicados',
      files:
        '{copied} copiados, {present} ya estaban y {failed} fallaron de {total} archivos · {size} de {totalSize}',
      waiting: 'Esperando a que se escaneen las bibliotecas para traer los datos de los usuarios…',
      brought:
        '{ratings} valoraciones, {plays} reproducciones, {playlists} playlists y {wishes} deseos · {unmatched} referencias sin encontrar.',
      failures: '{count} archivos no se pudieron copiar',
      cancel: 'Detener',
      retry: 'Continuar',
    },
    codes: {
      title: 'Migrar este servidor a otro',
      help: 'Crea un código para que otro Arcanist (un servidor nuevo, por ejemplo) se traiga este: usuarios, bibliotecas y música, datos, ajustes y extensiones. Leer este servidor así no cuenta reproducciones ni cambia nada aquí.',
      create: 'Crear un código de migración',
      active: 'Activo',
      revoked: 'Revocado',
      expired: 'Caducado',
      made: 'Creado {when}',
      until: 'válido hasta {when}',
      used: 'usado {when} por {by}',
      revoke: 'Revocar',
      createdTitle: 'Código de migración',
      createdHelp:
        'Escríbelo en el otro servidor (Importación → Desde otro Arcanist, o en su asistente inicial). Solo se muestra ahora. Quien lo tenga puede leer todo este servidor, contraseñas incluidas: compártelo solo con quien vaya a hacer la migración.',
      addresses: 'Direcciones de este servidor en tu red',
      link: 'Enlace P2P (sin abrir puertos)',
      linkHelp: 'Lleva el código dentro: pégalo en el otro servidor y no hace falta nada más.',
      noLink: 'Con la red P2P encendida también tendrías un enlace para migrar sin abrir puertos.',
      validUntil: 'Vale hasta {when}.',
    },
  },
  dlnaImport: {
    choose: 'Usar esta selección',
    title: 'Copiar desde un servidor DLNA',
    subtitle:
      'Trae la música de otro servidor de la red (MinimServer, Serviio, Universal Media Server, Twonky, un NAS…) a una biblioteca. El otro servidor no se modifica.',
    servers: {
      title: 'Servidores en la red',
      search: 'Buscar de nuevo',
      searching: 'Buscando servidores DLNA en la red…',
      none: 'No se encontró ningún servidor. Comprueba que esté encendido y en la misma red, o escribe su dirección.',
      byAddress: 'O escribe la dirección de su descripción',
      byAddressHelp:
        'Para servidores en otra red o que no responden a la búsqueda. Suele terminar en .xml; la indica el propio servidor.',
      open: 'Abrir',
    },
    browse: {
      change: 'Cambiar de servidor',
      loading: 'Leyendo la carpeta…',
      noFolders: 'No hay subcarpetas.',
      tracksHere: '{count} canciones directamente en esta carpeta',
      truncated: 'el servidor tiene más de las que se muestran',
      selectThis: 'Seleccionar esta carpeta',
      views:
        'Los servidores muestran la misma música de varias formas (carpetas, álbumes, artistas…). Basta con elegir una: cada canción se copia una sola vez.',
    },
    selected: 'Carpetas elegidas',
    destination: {
      folderRequired: 'Elige la carpeta de la biblioteca nueva.',
      title: 'Copiar a',
      existing: 'Una biblioteca existente',
      new: 'Una biblioteca nueva',
      name: 'Nombre de la biblioteca',
      folder: 'Carpeta de la biblioteca en este servidor',
    },
    notes: {
      originals:
        'Se descargan los archivos originales; solo si el servidor no los ofrece se usa una versión convertida.',
      template: 'Se colocan según la plantilla de la biblioteca, con la carátula del álbum.',
      duplicates: 'Lo que la biblioteca ya tiene no se vuelve a copiar.',
    },
    start: 'Copiar',
    startedToast: 'Copia iniciada',
    status: {
      QUEUED: 'En cola',
      LISTING: 'Buscando música',
      COPYING: 'Copiando',
      COMPLETED: 'Completada',
      FAILED: 'Fallida',
      CANCELLED: 'Cancelada',
    },
    history: {
      into: '{server} → {library}',
      folders: 'Carpetas: {folders}',
      listing: 'Buscando música en el servidor… ({found} encontradas)',
      report:
        '{copied} de {found} copiadas ({size}) · {duplicates} ya estaban · {failed} con errores',
      failures: 'Ver las que fallaron ({count})',
      cancel: 'Detener',
      retry: 'Reintentar lo que falta',
    },
  },
  mstream: {
    title: 'Importar desde mStream',
    subtitle:
      'Trae las bibliotecas, los usuarios (con sus contraseñas) y sus playlists, valoraciones y reproducciones de una instalación de mStream. mStream no se modifica.',
    browse: 'Examinar',
    analyze: 'Leer',
    analyzeAgain: 'Volver a leer',
    import: 'Importar',
    started: 'Importación iniciada',
    version: 'mStream {version}.',
    source: {
      title: 'Origen',
      folder: 'Una carpeta de este servidor',
      folderHelp:
        'La carpeta de instalación de mStream o su archivo de configuración. Se lee todo, contraseñas incluidas.',
      server: 'Un servidor mStream',
      serverHelp:
        'Por su API, con una cuenta de administrador: sus datos llegan ya y los de cada usuario la primera vez que inicia sesión aquí. Si está en otra máquina, sus archivos se pueden copiar.',
      folderLabel: 'Carpeta o archivo de configuración',
      url: 'Dirección del servidor',
      adminUser: 'Administrador de mStream',
      passwordHelp:
        'La contraseña no se guarda. Para copiar archivos se guarda la sesión de mStream, cifrada, hasta que termina la copia.',
    },
    libraries: {
      title: 'Bibliotecas',
      help: 'Elige qué será cada biblioteca de mStream. Sus archivos se buscan por su ruta dentro de la carpeta elegida.',
      helpSetup:
        'Las que importes se añaden a tus bibliotecas en el siguiente paso. Sus archivos se buscan por su ruta dentro de la carpeta elegida.',
      none: 'mStream no tiene bibliotecas.',
      notHere: 'No está en este servidor',
      files: '{count} canciones en mStream',
      copy: 'Copiar los archivos desde mStream',
      copyHelp:
        'Se descargan por su API a la carpeta elegida, con las mismas rutas y sin sobrescribir nada; después se escanea. Sigue en segundo plano y se reanuda tras un reinicio.',
      noCopyHere: 'Se usan los archivos que ya hay en esta carpeta.',
      noCopyEmpty: 'Sin copiarlos, la biblioteca solo tendrá lo que ya haya en la carpeta elegida.',
      skip: 'No importar',
      new: 'Nueva biblioteca',
      name: 'Nombre',
      path: 'Carpeta',
    },
    users: {
      title: 'Usuarios',
      helpFolder:
        'Los usuarios nuevos conservan su contraseña de mStream, ven las bibliotecas de mStream que tenían y pueden descargar. Los que ya existen aquí reciben sus datos sin otros cambios.',
      helpServer:
        'Los usuarios nuevos inician sesión con su contraseña de mStream (se comprueba con ese servidor la primera vez) y entonces llegan sus playlists, valoraciones y reproducciones.',
      exists: 'Ya existe',
      you: 'Tu cuenta',
      sameName:
        'Si tu usuario de administrador se llama igual que tu usuario de mStream, tus playlists, valoraciones y reproducciones pasan a tu cuenta.',
      noPassword: 'Sin contraseña: un administrador deberá asignarle una',
      none: 'mStream no tiene usuarios.',
      data: '{playlists} playlists, {rated} canciones valoradas y {played} reproducidas.',
      dataServer:
        'Tu cuenta de mStream: {playlists} playlists, {rated} canciones valoradas y {played} reproducidas. Las de los demás usuarios llegan cuando inician sesión.',
      import: 'Importar los usuarios y sus datos',
    },
    history: {
      title: 'Importaciones',
      report:
        '{libraries} bibliotecas y {users} usuarios creados · {playlists} playlists, {ratings} valoraciones y {plays} canciones reproducidas · {unmatched} sin encontrar',
      waiting: 'Esperando a que terminen los escaneos de las bibliotecas.',
      running: 'Trayendo playlists, valoraciones y reproducciones…',
      server: 'Los datos de cada usuario llegan cuando inicia sesión por primera vez.',
      files: '{copied} de {total} archivos copiados · {present} ya estaban · {failed} con error',
      copying: 'Copiando los archivos desde mStream…',
      retry: 'Reintentar los que fallaron',
    },
    status: {
      COPYING: 'Copiando',
      WAITING: 'En espera',
      RUNNING: 'En curso',
      COMPLETED: 'Completada',
      FAILED: 'Fallida',
    },
  },
  backups: {
    title: 'Copias de seguridad',
    subtitle:
      'Copias programadas del servidor, de sus bibliotecas, álbumes o playlists, en carpetas, buckets S3 u otros servidores Arcanist.',
    goToNetwork: 'Ir a Red P2P',
    failures: '{count} archivo(s) con error',
    finishedToast: 'Copia «{name}» terminada',
    failedToast: 'La copia «{name}» terminó con errores',
    runQueued: 'Copia «{name}» en marcha',
    deleted: 'Copia eliminada',
    deleteTitle: '¿Eliminar la copia «{name}»?',
    deleteHelp:
      'Deja de hacerse y desaparece de este servidor. Lo que ya está copiado en el destino se queda: se puede seguir restaurando desde ahí.',
    list: {
      title: 'Copias',
      description: 'Qué se copia, adónde y cuándo.',
      create: 'Nueva copia',
      needsDestination: 'Primero añade un destino: dónde se guardan las copias.',
      empty: 'Aún no hay copias',
      emptyHelp: 'Crea una para copiar el servidor entero o solo parte de su música.',
      paused: 'Pausada',
      to: 'En {destination}',
      next: 'próxima {when}',
      runNow: 'Ejecutar ahora',
      history: 'Historial',
      progress: '{copied} copiados · {unchanged} sin cambios · {bytes}',
      failedCount: '{count} con error',
      neverRun: 'Aún no se ha ejecutado',
    },
    runStatus: {
      QUEUED: 'En cola',
      RUNNING: 'Copiando',
      COMPLETED: 'Completada',
      COMPLETED_WITH_ERRORS: 'Con errores',
      FAILED: 'Fallida',
      CANCELLED: 'Cancelada',
    },
    counters: {
      files: 'Archivos',
      copied: 'Copiados',
      unchanged: 'Sin cambios',
      versioned: 'Versiones guardadas',
      skipped: 'Omitidos',
      failed: 'Con error',
    },
    runs: {
      title: 'Historial de «{name}»',
      description: 'Las últimas ejecuciones de esta copia.',
      trigger: {
        MANUAL: 'A mano',
        SCHEDULED: 'Programada',
      },
      started: 'Empezó',
      finished: 'Terminó',
      snapshot: 'Instantánea',
      empty: 'Aún no se ha ejecutado.',
    },
    scopes: {
      SERVER: {
        name: 'Servidor completo',
        help: 'Todas las bibliotecas de este servidor (salvo las personales de Ultimate Cloud) y, si quieres, su base de datos.',
      },
      LIBRARY: {
        name: 'Bibliotecas',
        help: 'Las bibliotecas que elijas, en este servidor o en S3.',
      },
      ALBUM: {
        name: 'Álbumes',
        help: 'Los archivos de los álbumes que elijas.',
      },
      PLAYLIST: {
        name: 'Playlists',
        help: 'Las canciones de las playlists que elijas.',
      },
    },
    days: {
      MONDAY: 'Lunes',
      TUESDAY: 'Martes',
      WEDNESDAY: 'Miércoles',
      THURSDAY: 'Jueves',
      FRIDAY: 'Viernes',
      SATURDAY: 'Sábado',
      SUNDAY: 'Domingo',
    },
    schedule: {
      title: 'Cuándo',
      kinds: {
        MANUAL: 'Solo a mano',
        HOURS: 'Cada N horas',
        DAILY: 'Cada día',
        WEEKLY: 'Cada semana',
      },
      every: 'Cada',
      hours: 'horas',
      day: 'Día de la semana',
      at: 'a las',
      timeZone: 'Zona horaria',
      timeZoneHelp: 'La hora se cuenta en esta zona (IANA, como Europe/Madrid).',
      intervalInvalid: 'El intervalo debe ser de 1 a 720 horas.',
      timeInvalid: 'Indica una hora válida.',
      summary: {
        manual: 'Solo a mano',
        everyHour: 'Cada hora',
        hours: 'Cada {count} horas',
        daily: 'Cada día a las {time}',
        weekly: 'Cada {day} a las {time}',
      },
    },
    form: {
      create: 'Nueva copia',
      edit: 'Editar copia',
      description:
        'Cada ejecución copia lo nuevo o cambiado; lo que cambia o desaparece se guarda como versión antigua.',
      name: 'Nombre',
      namePlaceholder: 'Copia nocturna',
      destination: 'Destino',
      destinationFixed: 'El destino no se puede cambiar: crea otra copia para usar otro.',
      scope: 'Qué se copia',
      libraries: 'Bibliotecas',
      noLibraries: 'No hay bibliotecas que se puedan copiar.',
      albums: 'Álbumes',
      albumSearch: 'Busca un álbum o un artista…',
      noAlbums: 'Ningún álbum coincide.',
      playlists: 'Playlists',
      noPlaylists: 'No hay playlists.',
      tracks: '{count} canción(es)',
      userData: 'Incluir datos de usuario',
      userDataHelp: 'Valoraciones, reproducciones y playlists de lo que se copia.',
      server: 'Incluir el servidor (base de datos, ajustes y claves)',
      serverHelp:
        'Permite restaurar este servidor entero, con sus cuentas, en otra instalación. Se guarda cifrado con una contraseña.',
      passwordWarning:
        'Guarda esta contraseña en un lugar seguro: sin ella la parte del servidor no se puede restaurar y Arcanist no puede recuperarla.',
      passwordKeep: 'Vacía para mantener la actual',
      passwordHelp: 'Al menos 8 caracteres.',
      passwordEditHelp:
        'Déjala vacía para mantener la actual. Una nueva solo vale para las copias que se hagan desde ahora.',
      passwordShort: 'La contraseña necesita al menos 8 caracteres.',
      passwordMismatch: 'Las contraseñas no coinciden.',
      retention: 'Versiones antiguas',
      days: 'días',
      retentionHelp:
        'Las versiones antiguas se guardan {days} días; después se borran del destino.',
      retentionForever: '0: las versiones antiguas se guardan para siempre.',
      enabled: 'Activa',
      enabledHelp: 'Pausada, no se ejecuta según su horario, pero sí a mano.',
      createAction: 'Crear copia',
      created: 'Copia creada',
      saved: 'Copia guardada',
    },
    destinations: {
      title: 'Destinos',
      description: 'Dónde se guardan las copias.',
      add: 'Añadir destino',
      addHelp: 'Arcanist comprueba que puede escribir en él antes de añadirlo.',
      kinds: {
        FOLDER: 'Carpeta del servidor',
        S3: 'Bucket S3',
        PEER: 'Otro servidor Arcanist',
      },
      kindHelp: {
        FOLDER:
          'Un disco, otra partición o una carpeta de red montada. No puede estar dentro de una biblioteca.',
        S3: 'Cualquier servicio compatible con S3 (AWS, Backblaze B2, Wasabi, Cloudflare R2, MinIO…). Necesita el plan Cloud.',
        PEER: 'Un servidor Arcanist que te haya invitado a guardar copias en él, a través de la red P2P.',
      },
      name: 'Nombre',
      namePlaceholder: 'Disco externo',
      peerNamePlaceholder: 'El nombre del otro servidor',
      peerNameHelp: 'Opcional: si lo dejas vacío, se usa el nombre del otro servidor.',
      folder: 'Carpeta',
      choose: 'Elegir…',
      folderHelp:
        'Una carpeta que exista, fuera de las bibliotecas y de la carpeta de datos de Arcanist.',
      s3NeedsPlan:
        'Guardar copias en S3 necesita el plan Cloud de una cuenta de Ultimate Cloud vinculada.',
      invitation: 'Invitación',
      invitationHelp:
        'Pega el enlace que te envió el administrador del otro servidor. Ambos necesitan la red P2P activa.',
      create: 'Añadir destino',
      created: 'Destino añadido',
      count: '{count} copia(s)',
      test: 'Probar',
      testOk: '«{name}» responde y se puede escribir en él',
      empty: 'Aún no hay destinos.',
      deleted: 'Destino eliminado',
      deleteTitle: '¿Eliminar el destino «{name}»?',
      deleteHelp:
        'Se eliminan de este servidor el destino y sus {count} copia(s). No se borra nada de lo que hay en el destino.',
    },
    restore: {
      title: 'Restaurar',
      description:
        'Trae de vuelta archivos y datos de usuario de una copia, de este servidor o de otro. Nunca se sobrescribe nada.',
      open: 'Restaurar…',
      needsDestination: 'Añade el destino donde está la copia.',
      empty: 'Aún no se ha restaurado nada.',
      startedToast: 'Restauración en marcha',
      stepOf: 'Paso {current} de {total}',
      steps: {
        destination: {
          title: '¿Dónde está la copia?',
          description:
            'El destino donde se guardó. También aparecen las copias que hicieron otros servidores.',
        },
        snapshot: {
          title: '¿Qué copia y de cuándo?',
          description: 'Cada fecha es una instantánea: los archivos tal como estaban entonces.',
        },
        what: {
          title: '¿Qué quieres restaurar?',
          description: 'Todo, o solo algunas bibliotecas, álbumes o playlists.',
        },
        where: {
          title: '¿Adónde van los archivos?',
          description: 'A las bibliotecas de las que salieron, o todos a una biblioteca.',
        },
        users: {
          title: '¿Para quién son los datos?',
          description:
            'Elige qué usuario de este servidor recibe las valoraciones, reproducciones y playlists de cada usuario de la copia.',
        },
        confirm: {
          title: 'Confirmar',
          description: 'Revisa lo que se va a restaurar.',
        },
      },
      madeBy: 'Hecha en {machine} · Arcanist {version}',
      files: '{count} archivo(s)',
      userDataBadge: 'Datos de usuario',
      serverBadge: 'Servidor',
      noSnapshots: 'Esta copia aún no tiene instantáneas.',
      noBackups: 'No hay copias en este destino.',
      what: {
        files: 'Archivos',
        filesHelp: 'La música, las carátulas y las letras que faltan o cambiaron.',
        userData: 'Datos de usuario',
        userDataHelp:
          'Valoraciones, reproducciones y playlists. Solo rellenan lo que falta: no se pierde nada de lo que ya hay.',
        which: 'Qué archivos',
        modes: {
          ALL: 'Todo',
          LIBRARIES: 'Algunas bibliotecas',
          ALBUMS: 'Algunos álbumes',
          PLAYLISTS: 'Las canciones de algunas playlists',
        },
        none: 'No hay nada de esto en la copia.',
        albumFilter: 'Filtra por álbum o artista…',
        chosen: '{count} elegido(s)',
        playlistsHelp: 'Se restauran los archivos de sus canciones.',
      },
      where: {
        original: 'A su ubicación original',
        originalHelp: 'Cada archivo vuelve a la biblioteca de la que salió.',
        other: 'A otra biblioteca',
        otherHelp: 'Todos los archivos van a la carpeta de una biblioteca de este servidor.',
        missing:
          'Estas bibliotecas de la copia no existen en este servidor: sus archivos no se pueden devolver a su sitio. Elige otra biblioteca para traerlos.',
        library: 'Biblioteca',
        noTargets: 'No hay bibliotecas en este servidor o en S3 donde restaurar.',
      },
      users: {
        counts: '{ratings} valoraciones · {plays} reproducciones · {playlists} playlists',
        to: 'Usuario de este servidor para {name}',
        skip: 'No restaurar',
      },
      confirm: {
        from: 'Copia:',
        what: 'Qué:',
        where: 'Adónde:',
        users: 'Usuarios:',
        usersCount: '{count} usuario(s) reciben datos',
        files: {
          ALL: 'Todos los archivos',
          LIBRARIES: 'Archivos de {count} biblioteca(s)',
          ALBUMS: 'Archivos de {count} álbum(es)',
          PLAYLISTS: 'Canciones de {count} playlist(s)',
        },
        noOverwrite:
          'No se sobrescribe nada: si en el sitio de un archivo hay otro distinto, ese se queda y el restaurado se guarda a su lado con «(restaurado AAAA-MM-DD)» en el nombre.',
      },
      start: 'Restaurar',
      status: {
        QUEUED: 'En cola',
        COPYING: 'Copiando archivos',
        WAITING_FOR_SCANS: 'Esperando a los escaneos',
        APPLYING: 'Restaurando datos',
        COMPLETED: 'Completada',
        COMPLETED_WITH_ERRORS: 'Con errores',
        FAILED: 'Fallida',
        CANCELLED: 'Cancelada',
      },
      counters: {
        restored: 'Restaurados',
        identical: 'Ya estaban',
        renamed: 'Renombrados',
        failed: 'Fallidos',
        ratings: 'Valoraciones',
        plays: 'Reproducciones',
        playlists: 'Playlists',
        unmatched: 'Sin coincidencia',
      },
    },
    guests: {
      title: 'Copias de otros servidores',
      description:
        'Otros servidores Arcanist que guardan sus copias aquí, a través de la red P2P. Para retirarles el acceso, revócalos en',
      revokeLink: 'Red P2P',
      invite: 'Invitar a un servidor',
      inviteHelp:
        'El otro servidor podrá guardar sus copias en una carpeta de este, a través de la red P2P.',
      empty: 'Ningún servidor guarda sus copias aquí.',
      revoked: 'Revocado',
      unknownUsage: 'Espacio desconocido',
      of: 'de {quota}',
      noQuota: 'sin límite',
      lastSeen: 'visto {when}',
      neverSeen: 'aún no se ha conectado',
      invitationTitle: 'Invitación creada',
      invitationHelp:
        'Envía este enlace al administrador del otro servidor: lo pega en Copias de seguridad › Añadir destino › Otro servidor Arcanist.',
      name: 'Nombre del otro servidor',
      namePlaceholder: 'Servidor de Ana',
      folder: 'Carpeta',
      folderHelp: 'Donde se guardan sus copias, fuera de las bibliotecas.',
      quota: 'Espacio máximo',
      quotaHelp: 'Opcional. Cuando se llena, sus copias se detienen.',
      needsP2p: 'Necesita la red P2P activa en los dos servidores.',
      createInvitation: 'Crear invitación',
    },
    overview: {
      title: 'Copias de seguridad',
      description: 'La última ejecución de cada copia y la próxima.',
      none: 'Aún no hay copias de seguridad.',
      open: 'Ver copias',
    },
  },
  errors: {
    terms_not_accepted: 'Hay que aceptar los términos y el descargo de responsabilidad.',
    ffmpeg_downloading: 'ffmpeg se está descargando: espera a que termine.',
    ffmpeg_in_use: 'ffmpeg se está usando ahora mismo. Inténtalo en un momento.',
    upload_library_required:
      'Elige una biblioteca: este servidor no tiene una predeterminada para las subidas.',
    migration_code_invalid: 'El código de migración no es válido, caducó o fue revocado.',
    migration_source_unreachable:
      'No se pudo llegar al otro servidor. Comprueba su dirección y que esté encendido.',
    migration_source_unsupported:
      'Ese servidor no es un Arcanist que se pueda migrar (o es de una versión que esta no entiende). Actualiza ambos.',
    wish_link_unreadable:
      'No se pudo leer la canción de ese enlace. Prueba con otro o escríbela a mano.',
    wish_link_not_a_song: 'Ese enlace no es de una canción (es un álbum, una lista o un artista).',
    external_services_disabled: 'La privacidad elegida no permite servicios externos.',
    telemetry_disabled: 'La privacidad elegida no permite enviar estadísticas.',
    maintenance:
      'El servidor está en mantenimiento (migrando su base de datos). Inténtalo en unos minutos.',
    busy: 'Hay trabajo en curso (escaneos, organización, copias): espera a que termine.',
    already_postgres: 'La base de datos ya es PostgreSQL.',
    verification_failed: 'La copia no coincide con el original: el servidor sigue con SQLite.',
    migration_failed:
      'La migración falló: el servidor sigue con SQLite. Revisa el registro del servidor.',
    cloud_tool_unavailable: 'Ultimate Cloud aún no ofrece la herramienta de audio.',
    cloud_tool_invalid:
      'La herramienta de audio recibida no está firmada por Ultimate o fue alterada: no se instaló.',
    cloud_tool_unsupported:
      'No hay herramienta de audio para esta plataforma, o necesita una versión más nueva de Arcanist.',
    cloud_tool_missing: 'La herramienta de audio de Ultimate no está instalada.',
    cloud_sync_unavailable: 'Sincronizar con Ultimate Cloud necesita el plan Cloud.',
    cloud_sync_download_taken:
      'Otra biblioteca ya baja de Ultimate Cloud: solo una puede, o las dos recibirían todo.',
    cloud_quota_exceeded: 'El almacenamiento de Ultimate Cloud está lleno: no se sube nada más.',
    cloud_read_only:
      'La cuenta de Ultimate Cloud está en solo lectura (suscripción vencida): no se sube nada.',
    upload_failed: 'La subida a Ultimate Cloud falló.',
    cloud_upload_cue:
      'La canción solo existe como parte de un archivo dividido por una hoja CUE, que no se puede subir por separado.',
    cloud_sync_failed:
      'La sincronización se detuvo por un error inesperado. Revisa el registro del servidor.',
    s3_unavailable: 'Las bibliotecas en S3 necesitan el plan Cloud.',
    s3_unreachable: 'El servicio S3 no responde en esa dirección.',
    s3_access_denied:
      'El servicio S3 rechazó las claves (incorrectas, o sin permiso en el bucket).',
    s3_bucket_not_found: 'No existe ese bucket en ese servicio (o en esa región).',
    cloud_not_configured: 'Este servidor no tiene configurada la dirección de Ultimate Cloud.',
    cloud_not_linked: 'No hay ninguna cuenta de Ultimate Cloud vinculada.',
    cloud_unreachable: 'No se puede contactar con Ultimate Cloud. Se volverá a intentar.',
    cloud_link_expired:
      'El acceso a Ultimate Cloud caducó o se revocó: vincula la cuenta de nuevo.',
    cloud_link_denied: 'Se rechazó el acceso desde Ultimate Cloud.',
    watch_unsupported: 'Esta biblioteca no se puede vigilar: escanéala con un intervalo.',
    backup_destination_unavailable:
      'No se puede escribir en el destino: la carpeta no existe, el disco está desconectado o el servidor no responde.',
    backup_destination_overlaps:
      'El destino no puede estar dentro de una biblioteca ni de la carpeta de datos del servidor.',
    backup_password_required:
      'Para copiar el servidor hace falta una contraseña de al menos 8 caracteres.',
    backup_password_invalid: 'La contraseña no abre esta copia.',
    backup_not_found: 'No hay esa copia en el destino.',
    backup_format_unsupported: 'La copia es de una versión más nueva de Arcanist, o está dañada.',
    backup_quota_exceeded: 'El espacio que el otro servidor reserva para tus copias está lleno.',
    backup_file_too_large:
      'El archivo es más grande de lo que acepta el destino de una vez (5 GB en S3).',
    backup_busy: 'La copia ya está en marcha o en cola.',
    backups_not_accepted:
      'El otro servidor no guarda las copias de este (nunca aceptó o dejó de hacerlo).',
    backup_failed: 'La copia falló por un error inesperado; revisa el registro del servidor.',
    backup_not_active: 'Solo se pueden cancelar las copias en cola o en marcha.',
    cloud_link_unsupported: 'Ultimate Cloud no permite vincular este servidor con un código.',
    dlna_server_unreachable: 'El servidor DLNA no responde.',
    not_media_server: 'Esa dirección no es la de un servidor DLNA/UPnP de medios.',
    unreadable: 'El archivo no se pudo leer como audio.',
    import_not_active: 'Esa importación no está en curso (o ya terminó).',
    import_running: 'Una importación en curso no se puede quitar del historial: cancélala primero.',
    import_in_use:
      'Algunos usuarios de ese servidor mStream aún no han iniciado sesión, y su primer inicio de sesión lo necesita.',
    import_failed:
      'La importación se detuvo por un error inesperado. Revisa el registro del servidor.',
    tags_unsupported: 'No se pueden escribir las etiquetas del formato de ese archivo.',
    cue_sheet_track:
      'Esa canción es parte de un álbum en un solo archivo con hoja CUE: sus etiquetas vienen de la hoja y el archivo tiene otras canciones.',
    unexpected: 'Algo salió mal. Inténtalo de nuevo.',
    network: 'No se puede contactar con el servidor.',
    unauthorized: 'Tu sesión ha caducado. Vuelve a iniciar sesión.',
    invalid_refresh_token: 'Tu sesión ha caducado. Vuelve a iniciar sesión.',
    forbidden: 'No tienes permiso para hacer esto.',
    not_found: 'No se encontró.',
    access_denied: 'El servidor no tiene permiso para leer esa carpeta.',
    invalid_credentials: 'Usuario o contraseña incorrectos.',
    account_disabled: 'Esta cuenta está desactivada.',
    too_many_requests: 'Demasiados intentos. Espera un momento.',
    reauthentication_required: 'Confirma tu contraseña para continuar.',
    validation_failed: 'Revisa los datos marcados.',
    required: 'Este campo es obligatorio.',
    invalid_format: 'El formato no es válido.',
    too_short: 'Es demasiado corto.',
    duplicate: 'Ya existe.',
    overlapping: 'Esa carpeta se solapa con otra: está dentro de ella o la contiene.',
    connection_failed: 'No se pudo conectar con la base de datos.',
    database_not_empty: 'Esa base de datos ya tiene datos de otra instalación.',
    setup_code_invalid: 'El código no es correcto.',
    setup_completed: 'El servidor ya está configurado.',
    setup_required: 'El servidor aún no está configurado.',
    last_administrator: 'Debe quedar al menos un administrador activo.',
    cannot_modify_self: 'No puedes hacer eso con tu propia cuenta.',
    scan_finished: 'El escaneo ya terminó.',
    scan_failed: 'El escaneo falló. Revisa el registro del servidor.',
    source_unavailable:
      'Alguna carpeta no estaba disponible (¿disco o unidad de red desconectados?). No se ha borrado nada.',
    interrupted: 'Se interrumpió al reiniciarse el servidor.',
    marketplace_unavailable: 'No se pudo leer el catálogo de extensiones. Inténtalo más tarde.',
    marketplace_incompatible: 'Esta extensión necesita una versión más reciente de Arcanist.',
    package_checksum_mismatch:
      'El paquete descargado no es el que publica el catálogo (su SHA-256 no coincide). No se ha instalado.',
    package_mismatch: 'El paquete descargado contiene otra extensión distinta de la del catálogo.',
    update_check_failed: 'No se pudo consultar si hay una versión nueva.',
    library_virtual: 'Es una biblioteca virtual (Todo): no se le puede subir ni cambiar nada.',
    plays_not_accepted: 'Ese servidor no cuenta las reproducciones de este.',
    not_ffmpeg: 'Ese archivo no es un ffmpeg que funcione.',
    ffmpeg_platform_unsupported:
      'No hay una descarga de ffmpeg para este sistema; instálalo con el gestor de paquetes e indica su ruta.',
    ffmpeg_download_failed:
      'No se pudo descargar ffmpeg. Comprueba la conexión a internet y vuelve a intentarlo.',
    transcoding_unavailable: 'No se puede convertir el audio ahora mismo.',
    read_only:
      'Arcanist no puede escribir en esa carpeta (permisos, o un montaje de solo lectura).',
    template_unknown_placeholder: 'La plantilla usa un dato que no existe.',
    template_title_required: 'La última parte de la plantilla debe incluir {title}.',
    write_failed: 'No se pudieron mover los archivos.',
    organize_running: 'La biblioteca ya se está organizando.',
    package_invalid: 'El archivo no es un paquete válido (un .zip seguro y no demasiado grande).',
    package_too_large: 'El paquete es demasiado grande.',
    manifest_missing: 'Al paquete le falta su manifiesto en la raíz.',
    manifest_invalid: 'El manifiesto del paquete no es válido.',
    api_version_unsupported: 'El paquete necesita una versión más reciente de Arcanist.',
    download_failed: 'No se pudo descargar desde esa dirección.',
    extension_failed: 'La extensión no se pudo cargar; revisa el registro del servidor.',
    route_not_allowed: 'La extensión usa rutas que no le corresponden.',
    route_conflict: 'Otra extensión activa ya usa esas rutas.',
    extension_managed:
      'La instaló la configuración del servidor: no se puede desactivar ni desinstalar desde aquí.',
    set_by_environment:
      'Ese ajuste viene del entorno (variables o archivos de configuración) y no se puede cambiar desde el panel.',
    too_many: 'Has llegado al máximo permitido.',
    playlist_full: 'Una playlist admite como mucho 10.000 canciones.',
    not_undoable: 'Eso ya no se puede deshacer.',
    mstream_unreachable: 'El servidor de mStream no responde, o no responde como mStream.',
    mstream_session_expired:
      'El servidor de mStream ya no acepta la sesión de esta importación: vuelve a importar para copiar lo que falta.',
    mstream_admin_required:
      'Hace falta una cuenta de administrador de mStream, con su API de administración desbloqueada.',
    dlna_disabled: 'DLNA está desactivado.',
    server_audio_disabled: 'El audio del servidor está desactivado.',
    ai_disabled: 'La extensión AI está desactivada.',
    analysis_unavailable:
      'No se puede analizar esa canción: sus copias están en fuentes que solo se analizan bajo pedido (desactivado) o no se pueden leer.',
    server_audio_unavailable:
      'El servidor no puede reproducir audio: falta su reproductor nativo o, en Linux, ALSA.',
    cloud_disabled: 'Ultimate Cloud está desactivado.',
    last_source: 'Una biblioteca local conserva su carpeta: muévela en lugar de quitarla.',
    library_managed:
      'Esta biblioteca la mantiene una extensión gestionada: no se puede eliminar ni cambiar su ubicación.',
    s3_wrong_key:
      'Lo que hay en ese bucket está cifrado con otra clave. Usa la clave con la que se creó.',
    library_read_only: 'En esta biblioteca solo puedes escuchar: no puedes subirle música.',
    closed_source_disabled:
      'El modo 100% open source está activado: los componentes de código cerrado no se pueden instalar ni usar.',
    library_disabled: 'Esta biblioteca necesita Ultimate Cloud, que está desactivado.',
    storage_conflict: 'En la carpeta nueva ya hay un archivo distinto con el mismo nombre.',
    cleanup_incomplete:
      'Movida, pero algunos archivos de la carpeta anterior no se pudieron borrar.',
    cloud_service_off: 'Ese servicio de Ultimate Cloud está desactivado.',
    renderer_unreachable: 'El dispositivo no responde: puede estar apagado o fuera de la red.',
    renderer_failed: 'El dispositivo rechazó la orden.',
    nothing_queued: 'Arcanist no tiene nada en cola en ese dispositivo.',
    renderer_in_use:
      'Otra persona está reproduciendo en ese dispositivo; podrás controlarlo cuando termine o lo detenga.',
    p2p_disabled: 'La red P2P está desactivada.',
    p2p_unavailable: 'La red P2P no está disponible en esta plataforma.',
    p2p_bind_failed: 'La red P2P no pudo abrir su puerto UDP (¿lo usa otro programa?).',
    peer_unreachable: 'El otro servidor no responde por la red P2P.',
    peer_unknown: 'Ese dispositivo o servidor no está emparejado con este servidor.',
    peer_revoked: 'El acceso de ese dispositivo o servidor fue revocado.',
    peer_expired: 'El acceso de ese dispositivo o servidor terminó.',
    invitation_invalid: 'La invitación no es válida, ya se usó o caducó.',
    too_many_streams: 'Demasiadas reproducciones a la vez por la red P2P.',
    download_not_allowed: 'El dueño de esa música no permite descargarla.',
    ffmpeg_checksum_mismatch:
      'El ffmpeg descargado no coincidía con su suma de comprobación y se ha descartado.',
  },
  videos: {
    title: 'Videoclips',
    subtitle:
      'Los videoclips de las bibliotecas: junto a su canción, con su nombre o subidos. Con la canción enlazada suenan con su audio de la biblioteca donde coincide.',
    search: 'Buscar por título o artista',
    find: 'Buscar',
    count: '{count} videoclips',
    none: 'No hay videoclips',
    noneHelp:
      'Pon archivos de video (MP4, MKV, WebM, MOV) en una biblioteca, junto a su canción y con el mismo nombre, o con «Artista - Título» en su nombre.',
    more: 'Ver más',
    unknownArtist: 'Artista desconocido',
    liveBadge: 'En vivo',
    live: 'Actuación en vivo (solo se enlaza con una pista en vivo)',
    duration: 'Duración',
    size: 'Tamaño',
    path: 'Archivo',
    coverage: 'La canción suena en el {percent} % del video',
    timelineHelp:
      'En color, los tramos que suenan con el audio de la canción; en gris, con el del video. Pulsa para saltar.',
    realign: 'Volver a sincronizar',
    realigning: 'Se sincronizará de nuevo en unos momentos.',
    delete: 'Eliminar',
    deleteConfirm: '¿Mover este videoclip a la papelera de su biblioteca? La canción no se toca.',
    filters: {
      all: 'Todos',
      linked: 'Con canción',
      unlinked: 'Sin canción',
      anyPerformance: 'Cualquier versión',
      live: 'En vivo',
      studio: 'De estudio',
    },
    audio: {
      label: 'Audio:',
      TRACK: 'Canción + video',
      VIDEO: 'Audio del video',
    },
    sync: {
      READY: 'Sincronizado',
      NO_MATCH: 'Otra versión',
      PENDING: 'Sincronizando',
      UNAVAILABLE: 'Sin sincronía',
    },
    link: {
      label: 'Canción',
      none: 'Sin canción',
      locked: 'Elegida a mano',
      search: 'Buscar la canción de este video',
      find: 'Buscar',
      remove: 'Quitar la canción',
      automatic: 'Volver a lo automático',
      SIDECAR: 'Junto a su archivo',
      TAGS: 'Por artista y título',
      FINGERPRINT: 'Por su audio',
      MANUAL: 'Elegida a mano',
    },
    settings: {
      title: 'Videoclips',
      description:
        'Qué audio suena por defecto con los videoclips, la caché de las versiones que se crean al reproducirlos y lo que se prepara antes para el streaming adaptativo.',
      counts:
        '{videos} videoclips, {linked} con canción, {aligned} sincronizados, {pending} por sincronizar.',
      defaultAudio: 'Audio por defecto',
      defaultAudioHelp:
        '«Canción + video» pone el audio de la biblioteca donde el video toca la canción y deja el del video en diálogos, intros y escenas. Cada usuario puede elegir otro en Mi cuenta, y los reproductores cambiarlo en vivo.',
      cache: 'Caché de versiones',
      usage: 'Caché: {cache}. Preparado: {prepared}.',
      prepare: 'Versiones preparadas (HLS)',
      prepareHelp:
        'Hasta UHD, nunca por encima del original. Con varias alturas, los reproductores eligen según el ancho de banda.',
      whenPlayed: 'Preparar también cada videoclip que alguien reproduce',
      libraries: 'Preparar siempre los videoclips de estas bibliotecas:',
    },
  },
};
