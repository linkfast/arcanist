import type { Tour } from './tour';

/**
 * The tours of the panel's sections. Steps point at `data-tour` attributes on the pages; one whose element is not
 * there (the user cannot see that part) shows in the middle. Each tour is short: what the section is for, then its
 * two or three key places.
 */
export const TOURS: Record<string, Tour> = {
  overview: {
    id: 'overview',
    steps: [
      {
        title: { es: 'Bienvenido al panel de Arcanist', en: 'Welcome to Arcanist’s panel' },
        body: {
          es: 'Aquí se administra el servidor: bibliotecas, usuarios, extensiones y todo lo demás. Cada sección tiene un tour corto como este la primera vez que la abres.',
          en: 'This is where the server is managed: libraries, users, extensions and everything else. Each section has a short tour like this one the first time you open it.',
        },
      },
      {
        target: '[data-tour="nav"]',
        title: { es: 'El menú', en: 'The menu' },
        body: {
          es: 'Las secciones que puedes usar según tus permisos. Las extensiones activas tienen su propio grupo.',
          en: 'The sections you can use with your permissions. Active extensions have a group of their own.',
        },
      },
      {
        target: '[data-tour="help"]',
        title: { es: 'Vuelve a ver un tour', en: 'See a tour again' },
        body: {
          es: 'Este botón, arriba a la derecha de cada sección, vuelve a mostrar su tour cuando quieras. Si saltas un tour o los desactivas todos, puedes volver a activarlos en Mi cuenta.',
          en: 'This button, at the top right of each section, shows its tour again whenever you want. If you skip a tour or turn them all off, you can turn them back on in My account.',
        },
      },
    ],
  },
  'lyrics-review': {
    id: 'lyrics-review',
    steps: [
      {
        title: { es: 'Revisar letras reconocidas', en: 'Reviewing recognized lyrics' },
        body: {
          es: 'Cuando una canción no tiene letra, Lyrics Sync puede escucharla y escribir lo que se canta. El modelo se equivoca a menudo: nadie ve esa letra hasta que alguien la corrige y la acepta aquí.',
          en: 'When a song has no lyrics, Lyrics Sync can listen to it and write down what is sung. The model often gets things wrong: nobody sees those lyrics until someone corrects and accepts them here.',
        },
      },
      {
        target: '[data-tour="lyrics-list"]',
        title: { es: 'Las canciones por revisar', en: 'The songs to review' },
        body: {
          es: 'El número en ámbar dice cuántas líneas dudosas tiene cada una: empieza por esas.',
          en: 'The amber number says how many doubtful lines each one has: start with those.',
        },
      },
      {
        target: '[data-tour="lyrics-player"]',
        title: { es: 'Escucha mientras corriges', en: 'Listen while you correct' },
        body: {
          es: 'La línea que se canta se ilumina. Pulsa el tiempo de una línea para oírla desde ahí, y la cruz (o Ctrl+Enter) para decir que empieza en este momento.',
          en: 'The line being sung lights up. Press a line’s time to hear it from there, and the crosshair (or Ctrl+Enter) to say it starts right now.',
        },
      },
      {
        target: '[data-tour="lyrics-lines"]',
        title: { es: 'Corrige las líneas', en: 'Correct the lines' },
        body: {
          es: 'Escribe sobre ellas: Enter parte una línea, Retroceso al principio la une a la anterior. Los cambios se guardan solos.',
          en: 'Type over them: Enter splits a line, Backspace at its start joins it to the one before. Changes save themselves.',
        },
      },
      {
        target: '[data-tour="lyrics-accept"]',
        title: { es: 'Acepta cuando esté bien', en: 'Accept when it is right' },
        body: {
          es: 'Desde ese momento es la letra de la canción para todos, con sus tiempos. Si la canción trae su propia letra más adelante, esa la reemplaza.',
          en: 'From then on they are the song’s lyrics for everyone, with their timing. If the song brings its own lyrics later, those replace them.',
        },
      },
    ],
  },
  libraries: {
    id: 'libraries',
    steps: [
      {
        title: { es: 'Tus bibliotecas', en: 'Your libraries' },
        body: {
          es: 'Cada biblioteca es una carpeta de música que Arcanist escanea y ofrece a los reproductores. Tú decides quién ve cada una.',
          en: 'Each library is a music folder Arcanist scans and offers to the players. You decide who sees each one.',
        },
      },
      {
        target: '[data-tour="libraries-create"]',
        title: { es: 'Crea una biblioteca', en: 'Create a library' },
        body: {
          es: 'Elige un nombre y una carpeta del servidor, si la ve todo el mundo y cada cuánto se escanea. Se escanea sola al crearla.',
          en: 'Pick a name and a server folder, whether everyone sees it and how often it is scanned. It is scanned as soon as you create it.',
        },
      },
      {
        target: '[data-tour="libraries-card"]',
        title: { es: 'Cada biblioteca', en: 'Each library' },
        body: {
          es: 'Su último escaneo, lo que guarda y su carpeta. El lápiz cambia sus ajustes; la papelera la borra, sin tocar tus archivos.',
          en: 'Its last scan, what it holds and its folder. The pencil changes its settings; the bin deletes it, without touching your files.',
        },
      },
      {
        target: '[data-tour="libraries-actions"]',
        title: { es: 'Escanear y organizar', en: 'Scan and organize' },
        body: {
          es: 'Escanear busca lo nuevo o cambiado; el escaneo completo lo relee todo. Organizar mueve los archivos a carpetas según una plantilla, con vista previa.',
          en: 'Scan looks for what is new or changed; a full scan reads everything again. Organize moves the files into folders by a template, with a preview.',
        },
      },
    ],
  },
  duplicates: {
    id: 'duplicates',
    steps: [
      {
        title: { es: 'Canciones repetidas', en: 'Repeated songs' },
        body: {
          es: 'Arcanist une solo las copias evidentes de una canción. Aquí quedan los casos dudosos y las canciones que están en varias bibliotecas.',
          en: 'Arcanist merges the obvious copies of a song by itself. The doubtful cases, and songs in several libraries, wait here.',
        },
      },
      {
        target: '[data-tour="duplicates-tabs"]',
        title: { es: 'Dos listas', en: 'Two lists' },
        body: {
          es: 'Posibles duplicados: canciones que suenan casi igual. Entre bibliotecas: la misma canción en dos o más, para liberar espacio.',
          en: 'Possible duplicates: songs that sound almost the same. Across libraries: the same song in two or more, to free space.',
        },
      },
      {
        target: '[data-tour="duplicates-list"]',
        title: { es: 'Decide cada par', en: 'Decide each pair' },
        body: {
          es: 'Conserva una para unir sus copias en una sola canción, o di que son distintas para que no se vuelvan a proponer.',
          en: 'Keep one to merge their copies into a single song, or say they are different so they are not suggested again.',
        },
      },
    ],
  },
  videos: {
    id: 'videos',
    steps: [
      {
        title: { es: 'Videoclips', en: 'Music videos' },
        body: {
          es: 'Los videos de tus bibliotecas. Enlazados a su canción, suenan con el audio de la biblioteca donde coinciden.',
          en: 'The videos in your libraries. Linked to their song, they play with the library’s audio where they match.',
        },
      },
      {
        target: '[data-tour="videos-filters"]',
        title: { es: 'Busca y filtra', en: 'Search and filter' },
        body: {
          es: 'Por título o artista, por si están enlazados a una canción y por si son en vivo o de estudio.',
          en: 'By title or artist, by whether they are linked to a song, and by live or studio.',
        },
      },
      {
        target: '[data-tour="videos-list"]',
        title: { es: 'Abre un videoclip', en: 'Open a music video' },
        body: {
          es: 'Pulsa uno para verlo, cambiar entre el audio de la canción y el del video, o enlazarlo a otra canción.',
          en: 'Press one to watch it, switch between the song’s audio and the video’s, or link it to another song.',
        },
      },
    ],
  },
  import: {
    id: 'import',
    steps: [
      {
        title: { es: 'Traer música', en: 'Bringing music in' },
        body: {
          es: 'Todo lo que entra en las bibliotecas desde fuera: otros servidores, carpetas de entrada y subidas.',
          en: 'Everything that comes into the libraries from outside: other servers, drop folders and uploads.',
        },
      },
      {
        target: '[data-tour="import-migrate"]',
        title: { es: 'Desde otro servidor', en: 'From another server' },
        body: {
          es: 'Trae bibliotecas, usuarios y datos de mStream, de un servidor DLNA o de otro Arcanist. El otro servidor no se modifica.',
          en: 'Bring libraries, users and data from mStream, a DLNA server or another Arcanist. The other server is not changed.',
        },
      },
      {
        target: '[data-tour="import-folders"]',
        title: { es: 'Carpetas de entrada', en: 'Drop folders' },
        body: {
          es: 'Lo que copies en ellas se identifica por sus etiquetas y pasa a una biblioteca cuando termina de copiarse.',
          en: 'What you copy into them is identified by its tags and moves into a library once it finishes copying.',
        },
      },
      {
        target: '[data-tour="import-uploads"]',
        title: { es: 'Subidas', en: 'Uploads' },
        body: {
          es: 'Todo lo que se sube desde aquí, las apps o las extensiones. «Subir música» deja arrastrar archivos a una biblioteca.',
          en: 'Everything uploaded from here, the apps or extensions. “Upload music” lets you drag files into a library.',
        },
      },
      {
        target: '[data-tour="import-queue"]',
        title: { es: 'Lo que espera tu decisión', en: 'What waits for you' },
        body: {
          es: 'Los archivos que no se pudieron colocar solos, por ejemplo sin etiquetas: complétalas, impórtalos tal cual o descártalos.',
          en: 'Files that could not be placed by themselves, for example without tags: fill them in, import them as they are or discard them.',
        },
      },
    ],
  },
  stats: {
    id: 'stats',
    steps: [
      {
        title: { es: 'Estadísticas', en: 'Statistics' },
        body: {
          es: 'Qué guarda el servidor y cuánto se escucha, de todos los usuarios.',
          en: 'What the server holds and how much it is played, for all users.',
        },
      },
      {
        target: '[data-tour="stats-filters"]',
        title: { es: 'Elige qué mirar', en: 'Choose what to look at' },
        body: {
          es: 'Una biblioteca o todas, y el periodo: semana, mes, año o siempre.',
          en: 'One library or all of them, and the period: week, month, year or all time.',
        },
      },
      {
        target: '[data-tour="stats-library"]',
        title: { es: 'La biblioteca', en: 'The library' },
        body: {
          es: 'Canciones, álbumes, tamaño y los formatos de los archivos.',
          en: 'Songs, albums, size and the files’ formats.',
        },
      },
      {
        target: '[data-tour="stats-listening"]',
        title: { es: 'Lo que se escucha', en: 'What is played' },
        body: {
          es: 'Reproducciones, saltadas, géneros, horas y días de la semana del periodo elegido.',
          en: 'Plays, skips, genres, hours and days of the week in the chosen period.',
        },
      },
      {
        target: '[data-tour="stats-users"]',
        title: { es: 'Por usuario', en: 'By user' },
        body: {
          es: 'Cuánto escucha cada uno, incluidos los oyentes de otros servidores que escuchan tus bibliotecas compartidas.',
          en: 'How much each one plays, including listeners on other servers who play your shared libraries.',
        },
      },
    ],
  },
  users: {
    id: 'users',
    steps: [
      {
        title: { es: 'Usuarios', en: 'Users' },
        body: {
          es: 'Las cuentas del servidor: quién entra, qué puede hacer y qué bibliotecas escucha.',
          en: 'The server’s accounts: who signs in, what they can do and which libraries they play.',
        },
      },
      {
        target: '[data-tour="users-create"]',
        title: { es: 'Nuevo usuario', en: 'New user' },
        body: {
          es: 'Crea una cuenta con su contraseña, su rol, sus permisos y las bibliotecas a las que accede.',
          en: 'Create an account with its password, role, permissions and the libraries it can reach.',
        },
      },
      {
        target: '[data-tour="users-tabs"]',
        title: { es: 'Cuentas y acceso', en: 'Accounts and access' },
        body: {
          es: 'Usuarios lista las cuentas; Acceso a bibliotecas dice, por usuario, qué bibliotecas escucha y en cuáles puede subir.',
          en: 'Users lists the accounts; Library access says, per user, which libraries they play and which they can upload to.',
        },
      },
      {
        target: '[data-tour="users-table"]',
        title: { es: 'Cada cuenta', en: 'Each account' },
        body: {
          es: 'Desde sus botones la editas, le pones una contraseña nueva o la eliminas.',
          en: 'Its buttons edit it, set a new password or delete it.',
        },
      },
    ],
  },
  settings: {
    id: 'settings',
    steps: [
      {
        title: { es: 'Ajustes del servidor', en: 'Server settings' },
        body: {
          es: 'Cómo se comporta el servidor para todos: valoraciones, privacidad, servicios en línea y mantenimiento.',
          en: 'How the server behaves for everyone: ratings, privacy, online services and maintenance.',
        },
      },
      {
        target: '[data-tour="settings-ratings"]',
        title: { es: 'Valoraciones', en: 'Ratings' },
        body: {
          es: 'Una estrella (StarTracks) o de una a cinco, en todos los reproductores. Puedes cambiarlo cuando quieras.',
          en: 'One star (StarTracks) or one to five, in every player. You can change it any time.',
        },
      },
      {
        target: '[data-tour="settings-privacy"]',
        title: { es: 'Privacidad', en: 'Privacy' },
        body: {
          es: 'Qué sale de este servidor. Lo que excluyes aquí se apaga y no se puede encender en otro sitio.',
          en: 'What leaves this server. What you leave out here is switched off and cannot be switched on elsewhere.',
        },
      },
      {
        target: '[data-tour="settings-online"]',
        title: { es: 'Servicios en línea', en: 'Online services' },
        body: {
          es: 'Completan tu biblioteca en segundo plano: identifican canciones y álbumes, y traen carátulas y letras.',
          en: 'They complete your library in the background: they identify songs and albums, and fetch artwork and lyrics.',
        },
      },
      {
        target: '[data-tour="settings-sharing"]',
        title: { es: 'Compartir', en: 'Sharing' },
        body: {
          es: 'Decide si tus usuarios pueden compartir playlists con un enlace público y abrir sus Jukebox Jams a invitados sin cuenta. Empieza apagado.',
          en: 'Choose whether your users can share playlists with a public link and open their Jukebox Jams to guests without an account. It starts off.',
        },
      },
      {
        target: '[data-tour="settings-maintenance"]',
        title: { es: 'Mantenimiento', en: 'Maintenance' },
        body: {
          es: 'Vuelve a pasar por el asistente o restablece el servidor. Tus archivos de música nunca se tocan.',
          en: 'Go through the setup wizard again or reset the server. Your music files are never touched.',
        },
      },
    ],
  },
  transcoding: {
    id: 'transcoding',
    steps: [
      {
        title: { es: 'Transcodificación', en: 'Transcoding' },
        body: {
          es: 'Arcanist convierte las canciones al formato y la calidad que pide cada reproductor, por ejemplo al escuchar fuera de casa.',
          en: 'Arcanist converts songs to the format and quality each player asks for, for example when listening away from home.',
        },
      },
      {
        target: '[data-tour="transcoding-ffmpeg"]',
        title: { es: 'ffmpeg', en: 'ffmpeg' },
        body: {
          es: 'El programa que convierte. Si no lo encuentra, puedes descargarlo desde aquí.',
          en: 'The program that does the converting. If it is not found, you can download it from here.',
        },
      },
      {
        target: '[data-tour="transcoding-clients"]',
        title: { es: 'Calidad de las apps', en: 'Quality of the apps' },
        body: {
          es: 'Lo que reciben las apps de Ultimate cuando el usuario no elige otra calidad: el original o una versión más ligera.',
          en: 'What the Ultimate apps get when their user picks no other quality: the original or a lighter version.',
        },
      },
      {
        target: '[data-tour="transcoding-conversion"]',
        title: { es: 'Guardar lo convertido', en: 'Keep what is converted' },
        body: {
          es: 'Guarda las conversiones para no repetirlas, o prepáralas de antemano para las bibliotecas que elijas.',
          en: 'Keep conversions so they are not repeated, or prepare them ahead of time for the libraries you choose.',
        },
      },
    ],
  },
  extensions: {
    id: 'extensions',
    steps: [
      {
        title: { es: 'Extensiones', en: 'Extensions' },
        body: {
          es: 'Añaden funciones al servidor, como DLNA, Subsonic o mStream, y los reproductores web son las páginas desde donde se escucha.',
          en: 'They add features to the server, such as DLNA, Subsonic or mStream, and web players are the pages people listen from.',
        },
      },
      {
        target: '[data-tour="extensions-tabs"]',
        title: { es: 'Tres pestañas', en: 'Three tabs' },
        body: {
          es: 'Instaladas, el Marketplace para instalar con un clic, y los Reproductores web.',
          en: 'Installed, the Marketplace to install with one click, and the web Players.',
        },
      },
      {
        target: '[data-tour="extensions-list"]',
        title: { es: 'Activa o apaga', en: 'Turn them on or off' },
        body: {
          es: 'Apagar una se aplica al momento. Encender una instalada puede pedir reiniciar el servidor: arriba aparece «Reiniciar ahora».',
          en: 'Turning one off applies right away. Turning an installed one on may need a server restart: “Restart now” shows up above.',
        },
      },
      {
        target: '[data-tour="extensions-install"]',
        title: { es: 'Instala desde un archivo', en: 'Install from a file' },
        body: {
          es: 'Suelta el .zip de una extensión o pega su URL. Tiene acceso total al servidor: instala solo las que confíes.',
          en: 'Drop an extension’s .zip or paste its URL. It gets full access to the server: install only ones you trust.',
        },
      },
    ],
  },
  'extension-page': {
    id: 'extension-page',
    steps: [
      {
        title: { es: 'Página de la extensión', en: 'The extension’s page' },
        body: {
          es: 'Cada extensión instalada describe aquí su estado, sus ajustes y lo que puedes hacer con ella.',
          en: 'Each installed extension shows its status, its settings and what you can do with it here.',
        },
      },
      {
        target: '[data-tour="extension-page-setup"]',
        title: { es: 'Lo que le falta', en: 'What it still needs' },
        body: {
          es: 'Si la extensión necesita descargar algo para funcionar, aparece aquí con el botón que lo instala.',
          en: 'If the extension needs to download something to work, it shows up here with the button that installs it.',
        },
      },
      {
        target: '[data-tour="extension-page-section"]',
        title: { es: 'Estado y ajustes', en: 'Status and settings' },
        body: {
          es: 'Cada tarjeta agrupa estados, campos y acciones. Las acciones se ejecutan al momento; algunas piden confirmar.',
          en: 'Each card groups statuses, fields and actions. Actions run right away; some ask you to confirm.',
        },
      },
      {
        target: '[data-tour="extension-page-save"]',
        title: { es: 'Guarda los cambios', en: 'Save your changes' },
        body: {
          es: 'Los campos que cambies no se aplican hasta que pulses Guardar.',
          en: 'The fields you change do not apply until you press Save.',
        },
      },
    ],
  },
  database: {
    id: 'database',
    steps: [
      {
        title: { es: 'Base de datos', en: 'Database' },
        body: {
          es: 'Dónde guarda Arcanist su catálogo, usuarios y ajustes, y cuánto ocupa cada parte.',
          en: 'Where Arcanist keeps its catalog, users and settings, and how much space each part takes.',
        },
      },
      {
        target: '[data-tour="database-current"]',
        title: { es: 'La que usas ahora', en: 'The one in use' },
        body: {
          es: 'SQLite o PostgreSQL, dónde está y su tamaño.',
          en: 'SQLite or PostgreSQL, where it is and its size.',
        },
      },
      {
        target: '[data-tour="database-migrate"]',
        title: { es: 'Pasar a PostgreSQL', en: 'Moving to PostgreSQL' },
        body: {
          es: 'Con bibliotecas muy grandes o muchos usuarios, copia todo a PostgreSQL y el servidor pasa a usarlo, sin reiniciar.',
          en: 'With very large libraries or many users, copy everything to PostgreSQL and the server switches to it, without restarting.',
        },
      },
    ],
  },
  backups: {
    id: 'backups',
    steps: [
      {
        title: { es: 'Copias de seguridad', en: 'Backups' },
        body: {
          es: 'Copias programadas del servidor, de bibliotecas, álbumes o playlists, en una carpeta, un bucket S3 u otro Arcanist.',
          en: 'Scheduled backups of the server, libraries, albums or playlists, to a folder, an S3 bucket or another Arcanist.',
        },
      },
      {
        target: '[data-tour="backups-destinations"]',
        title: { es: 'Primero, un destino', en: 'First, a destination' },
        body: {
          es: 'Dónde se guardan las copias. Arcanist comprueba que puede escribir en él antes de añadirlo.',
          en: 'Where backups are kept. Arcanist checks it can write there before adding it.',
        },
      },
      {
        target: '[data-tour="backups-list"]',
        title: { es: 'Las copias', en: 'The backups' },
        body: {
          es: 'Qué se copia, adónde y cuándo. Cada copia guarda versiones de lo que cambia, y puedes ejecutarla cuando quieras.',
          en: 'What is backed up, where and when. Each backup keeps versions of what changes, and you can run it whenever you want.',
        },
      },
      {
        target: '[data-tour="backups-restore"]',
        title: { es: 'Restaurar', en: 'Restore' },
        body: {
          es: 'Trae de vuelta archivos y datos de usuario de una copia, de este servidor o de otro. Nunca se sobrescribe nada.',
          en: 'Bring back files and user data from a backup, made by this server or another. Nothing is ever overwritten.',
        },
      },
      {
        target: '[data-tour="backups-guests"]',
        title: { es: 'Copias de otros servidores', en: 'Other servers’ backups' },
        body: {
          es: 'Otros Arcanist pueden guardar aquí sus copias por la red P2P, en la carpeta y con el espacio que elijas al invitarlos.',
          en: 'Other Arcanist servers can keep their backups here through the P2P network, in the folder and space you choose when inviting them.',
        },
      },
    ],
  },
  cloud: {
    id: 'cloud',
    steps: [
      {
        title: { es: 'Ultimate Cloud', en: 'Ultimate Cloud' },
        body: {
          es: 'Vincula el servidor a tu cuenta de Ultimate Cloud para usar tu música en la nube y las funciones de pago. Todo lo local sigue siendo gratis.',
          en: 'Link the server to your Ultimate Cloud account to use your music in the cloud and the paid features. Everything local stays free.',
        },
      },
      {
        target: '[data-tour="cloud-switch"]',
        title: { es: 'Encender o apagar', en: 'On or off' },
        body: {
          es: 'Si no usas Ultimate Cloud, apágalo: todo lo suyo desaparece del panel. Se vuelve a activar desde Extensiones.',
          en: 'If you do not use Ultimate Cloud, turn it off: everything of it leaves the panel. It is turned back on from Extensions.',
        },
      },
      {
        target: '[data-tour="cloud-account"]',
        title: { es: 'Vincula tu cuenta', en: 'Link your account' },
        body: {
          es: 'Apruebas el acceso en el sitio de Ultimate Cloud con un código: Arcanist nunca ve tu contraseña.',
          en: 'You approve the access on Ultimate Cloud’s site with a code: Arcanist never sees your password.',
        },
      },
      {
        target: '[data-tour="cloud-capabilities"]',
        title: { es: 'Lo que activa tu plan', en: 'What your plan enables' },
        body: {
          es: 'Las funciones que la cuenta vinculada desbloquea en este servidor.',
          en: 'The features the linked account unlocks on this server.',
        },
      },
    ],
  },
  network: {
    id: 'network',
    steps: [
      {
        title: { es: 'Red P2P', en: 'P2P network' },
        body: {
          es: 'Llega a tu servidor desde cualquier sitio sin abrir puertos, y comparte bibliotecas con otros Arcanist. Todo va cifrado.',
          en: 'Reach your server from anywhere without opening ports, and share libraries with other Arcanist servers. Everything is encrypted.',
        },
      },
      {
        target: '[data-tour="network-enable"]',
        title: { es: 'Enciende la red', en: 'Turn the network on' },
        body: {
          es: 'Viene apagada. Nada es accesible hasta que emparejas un dispositivo o compartes una biblioteca.',
          en: 'It starts off. Nothing can be reached until you pair a device or share a library.',
        },
      },
      {
        target: '[data-tour="network-node"]',
        title: { es: 'Tu nodo', en: 'Your node' },
        body: {
          es: 'Su nombre, cómo conectar cuando no hay conexión directa y los límites de ancho de banda y de streams.',
          en: 'Its name, how to connect when there is no direct path, and the bandwidth and stream limits.',
        },
      },
      {
        target: '[data-tour="network-tabs"]',
        title: { es: 'Servidores, dispositivos y actividad', en: 'Servers, devices and activity' },
        body: {
          es: 'Comparte bibliotecas con otros servidores, revisa los dispositivos emparejados y consulta quién usó la red.',
          en: 'Share libraries with other servers, review the paired devices and see who used the network.',
        },
      },
    ],
  },
  dlna: {
    id: 'dlna',
    steps: [
      {
        title: { es: 'DLNA', en: 'DLNA' },
        body: {
          es: 'Televisores, consolas, Kodi o VLC de tu red local navegan y reproducen tu música, y Arcanist puede reproducir en altavoces y televisores.',
          en: 'TVs, consoles, Kodi or VLC on your local network browse and play your music, and Arcanist can play to speakers and TVs.',
        },
      },
      {
        target: '[data-tour="dlna-server"]',
        title: { es: 'El servidor DLNA', en: 'The DLNA server' },
        body: {
          es: 'Enciéndelo aquí. DLNA no tiene inicio de sesión: cualquier dispositivo de tu red ve lo que publiques.',
          en: 'Turn it on here. DLNA has no sign-in: any device on your network sees what you publish.',
        },
      },
      {
        target: '[data-tour="dlna-settings"]',
        title: { es: 'Lo que ven los dispositivos', en: 'What devices see' },
        body: {
          es: 'Elige las bibliotecas publicadas y las carpetas del menú, en el orden que quieras, y pulsa Guardar.',
          en: 'Choose the published libraries and the menu folders, in the order you like, and press Save.',
        },
      },
      {
        target: '[data-tour="dlna-renderers"]',
        title: { es: 'Reproducir en', en: 'Play to' },
        body: {
          es: 'Los altavoces y televisores de la red: envíales un álbum y contrólalo desde aquí.',
          en: 'The speakers and TVs on the network: send them an album and control it from here.',
        },
      },
    ],
  },
  ai: {
    id: 'ai',
    steps: [
      {
        title: { es: 'AI', en: 'AI' },
        body: {
          es: 'Analiza cada canción para software de DJ y clientes con IA, como el AI DJ de ULTIMATE: beats, secciones, energía, voz y puntos de mezcla.',
          en: 'Analyzes every song for DJ software and AI clients, such as the ULTIMATE AI DJ: beats, sections, energy, voice and mix points.',
        },
      },
      {
        target: '[data-tour="ai-analysis"]',
        title: { es: 'El análisis', en: 'The analysis' },
        body: {
          es: 'Enciéndelo aquí y sigue su progreso. Las canciones locales se analizan en segundo plano; las demás, cuando un cliente las pide.',
          en: 'Turn it on here and follow its progress. Local songs are analyzed in the background; the rest, when a client asks for them.',
        },
      },
      {
        target: '[data-tour="ai-performance"]',
        title: { es: 'Rendimiento', en: 'Performance' },
        body: {
          es: 'Cuántas canciones analiza a la vez y si se pausa mientras se escanea o alguien escucha.',
          en: 'How many songs it analyzes at once and whether it pauses while scanning or while someone listens.',
        },
      },
      {
        target: '[data-tour="ai-inspector"]',
        title: { es: 'Inspector', en: 'Inspector' },
        body: {
          es: 'Busca una canción para ver su análisis, y pulsa en cualquier punto para oír ese momento.',
          en: 'Search for a song to see its analysis, and click anywhere to hear that moment.',
        },
      },
    ],
  },
  'server-audio': {
    id: 'server-audio',
    steps: [
      {
        title: { es: 'Audio del servidor', en: 'Server audio' },
        body: {
          es: 'El servidor reproduce por un dispositivo conectado a él: altavoces, un DAC USB o HDMI.',
          en: 'The server plays through a device attached to it: speakers, a USB DAC or HDMI.',
        },
      },
      {
        target: '[data-tour="server-audio-output"]',
        title: { es: 'La salida', en: 'The output' },
        body: {
          es: 'Enciéndela aquí. Aparece en «reproducir en» de los reproductores para los administradores y quien tenga permiso.',
          en: 'Turn it on here. It shows up in the players’ “play to” for administrators and anyone with permission.',
        },
      },
      {
        target: '[data-tour="server-audio-device"]',
        title: { es: 'Dispositivo', en: 'Device' },
        body: {
          es: 'Por dónde suena y con qué nombre se muestra. «Probar» hace sonar un tono.',
          en: 'Where it plays and the name it shows. “Test” plays a tone.',
        },
      },
      {
        target: '[data-tour="server-audio-playing"]',
        title: { es: 'Lo que suena', en: 'Now playing' },
        body: {
          es: 'Controla la cola o pon un álbum desde aquí. Las reproducciones cuentan para quien la empezó.',
          en: 'Control the queue or play an album from here. Plays count for whoever started it.',
        },
      },
    ],
  },
  mstream: {
    id: 'mstream',
    steps: [
      {
        title: { es: 'mStream', en: 'mStream' },
        body: {
          es: 'Las apps de mStream pueden usar este servidor. Aquí decides cómo se comporta con ellas.',
          en: 'The mStream apps can use this server. Here you decide how it behaves with them.',
        },
      },
      {
        target: '[data-tour="mstream-sign-in"]',
        title: { es: 'Inicio de sesión', en: 'Signing in' },
        body: {
          es: 'Entran con una contraseña de aplicación de Mi cuenta. Si quieres, también con la contraseña de cada usuario.',
          en: 'They sign in with an app password from My account. If you want, also with each user’s own password.',
        },
      },
      {
        target: '[data-tour="mstream-quick-connect"]',
        title: { es: 'Conexión rápida', en: 'Quick connect' },
        body: {
          es: 'Las apps se conectan escaneando un código por la red P2P, sin abrir puertos ni conocer la dirección del servidor.',
          en: 'The apps connect by scanning a code through the P2P network, with no open ports or server address to know.',
        },
      },
      {
        target: '[data-tour="mstream-features"]',
        title: { es: 'Funciones de las apps', en: 'The apps’ functions' },
        body: {
          es: 'Forma de onda, estadísticas, similares… Apagadas, las apps las ocultan.',
          en: 'Waveform, statistics, similar songs… Switched off, the apps hide them.',
        },
      },
    ],
  },
  wishes: {
    id: 'wishes',
    steps: [
      {
        title: { es: 'Lista de deseos', en: 'Wish list' },
        body: {
          es: 'Las canciones que quieres y aún no tienes. Cuando una llega a una biblioteca que escuchas, se reconoce sola y te avisamos.',
          en: 'Songs you want and do not have yet. When one reaches a library you play, it is recognised by itself and you are told.',
        },
      },
      {
        target: '[data-tour="wishes-link"]',
        title: { es: 'Desde un enlace', en: 'From a link' },
        body: {
          es: 'Pega el enlace de una canción de Spotify, YouTube Music, Deezer, Apple Music… y se rellena sola, con su carátula.',
          en: 'Paste a song’s link from Spotify, YouTube Music, Deezer, Apple Music… and it fills itself in, with its cover.',
        },
      },
      {
        target: '[data-tour="wishes-add"]',
        title: { es: 'O a mano', en: 'Or by hand' },
        body: {
          es: 'Título y artista bastan; el álbum y la duración ayudan a reconocerla.',
          en: 'Title and artist are enough; the album and length help recognise it.',
        },
      },
      {
        target: '[data-tour="wishes-wanted"]',
        title: { es: 'La busco', en: 'Wanted' },
        body: {
          es: 'Lo que aún esperas. Cuando llega, pasa a Llegaron.',
          en: 'What you are still waiting for. When it arrives, it moves to Arrived.',
        },
      },
    ],
  },
  account: {
    id: 'account',
    steps: [
      {
        title: { es: 'Mi cuenta', en: 'My account' },
        body: {
          es: 'Lo tuyo en este servidor: contraseña, sesiones, contraseñas de aplicación y tus preferencias.',
          en: 'What is yours on this server: password, sessions, app passwords and your preferences.',
        },
      },
      {
        target: '[data-tour="account-sessions"]',
        title: { es: 'Sesiones abiertas', en: 'Open sessions' },
        body: {
          es: 'Dónde tienes la sesión iniciada. Cierra las que no reconozcas.',
          en: 'Where you are signed in. Sign out of any you do not recognise.',
        },
      },
      {
        target: '[data-tour="account-app-passwords"]',
        title: { es: 'Contraseñas de aplicación', en: 'App passwords' },
        body: {
          es: 'Crea una para cada app de Subsonic o mStream: tu contraseña nunca sale de aquí.',
          en: 'Create one for each Subsonic or mStream app: your password never leaves here.',
        },
      },
      {
        target: '[data-tour="account-tours"]',
        title: { es: 'Tours del panel', en: 'Panel tours' },
        body: {
          es: 'Si los desactivaste o saltaste alguno, vuelve a activarlos aquí, o pide ver todos otra vez.',
          en: 'If you turned them off or skipped one, turn them back on here, or ask to see them all again.',
        },
      },
    ],
  },
};
