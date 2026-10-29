using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// The game's languages: English (as written in the code), Persian, Arabic and Spanish. The menus and
    /// the battle screen ask for each English text with T (or F with values); Persian and Arabic come back
    /// joined and right to left (Rtl). A text without a translation stays English.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] LanguageNames = { "ENGLISH", "فارسی", "العربية", "ESPAÑOL" };
        public static int Lang => Settings.Language;
        public static bool RightToLeft => Lang == 1 || Lang == 2;

        private static Dictionary<string, string>[] table;

        /// <summary>A language name as shown on the settings page (joined, in its own script).</summary>
        public static string LanguageName(int lang) => Rtl.Shape(LanguageNames[Mathf.Clamp(lang, 0, LanguageNames.Length - 1)]);

        /// <summary>The text in the player's language (whole, or line by line, or part by part around " - ").</summary>
        public static string T(string en)
        {
            if (Lang == 0 || string.IsNullOrEmpty(en)) return en;
            var words = Table();
            if (words.TryGetValue(en, out var tr)) return Shape(tr);
            foreach (string cut in new[] { "\n", " - " })
            {
                if (!en.Contains(cut)) continue;
                var parts = en.Split(new[] { cut }, System.StringSplitOptions.None);
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                    if (words.TryGetValue(parts[i], out var p)) { parts[i] = Shape(p); any = true; }
                if (any) return string.Join(cut, parts);
            }
            return en;
        }

        /// <summary>The translation as written (not yet joined or turned right to left), or the English.</summary>
        public static string Raw(string en)
        {
            if (Lang == 0 || string.IsNullOrEmpty(en)) return en;
            return Table().TryGetValue(en, out var tr) ? tr : en;
        }

        /// <summary>A text with values in it ({0}, {1} ...): the values put in first, then joined as a whole.</summary>
        public static string F(string en, params object[] values)
        {
            if (Lang == 0 || !Table().TryGetValue(en, out var tr)) return string.Format(en, values);
            return Shape(string.Format(tr, values));
        }

        /// <summary>
        /// "LABEL:  value" in the reading order of the language: for right-to-left the value stands on the
        /// left (the line is drawn left to right). The value is shown as given (a name, a number, coloured text).
        /// </summary>
        public static string Pair(string label, string value) => RightToLeft ? value + "  :" + T(label) : T(label) + ":  " + value;

        /// <summary>A name (or coloured text) and a word after it, e.g. "BLUE WINS", in the reading order of the language.</summary>
        public static string After(string name, string word) => RightToLeft ? T(word) + " " + name : name + " " + T(word);

        private static string Shape(string text) => RightToLeft ? Rtl.Shape(text) : text;

        private static Dictionary<string, string> Table()
        {
            if (table == null)
            {
                table = new Dictionary<string, string>[4];
                for (int l = 1; l < 4; l++) table[l] = new Dictionary<string, string>();
                foreach (var line in Words.Split('\n'))
                {
                    var cols = line.Trim('\r').Split('|');
                    if (cols.Length != 4) continue;
                    string key = cols[0].Replace("\\n", "\n");
                    for (int l = 1; l < 4; l++) table[l][key] = cols[l].Replace("\\n", "\n");
                }
            }
            return table[Mathf.Clamp(Lang, 1, 3)];
        }

        // English | Persian | Arabic | Spanish (no Arabic comma or question mark: the font has none)
        private const string Words = @"
PLAY ON WI-FI|بازی با وای فای|اللعب عبر الواي فاي|JUGAR POR WI-FI
PRACTICE VS BOTS|تمرین با ربات ها|تدريب ضد الروبوتات|PRÁCTICA CONTRA BOTS
PROFILE|پروفایل|الملف الشخصي|PERFIL
SETTINGS|تنظیمات|الإعدادات|AJUSTES
QUIT|خروج|خروج|SALIR
Squad up on the same Wi-Fi or hotspot. No internet needed.|با دوستانت روی یک وای فای یا هات اسپات بازی کن. اینترنت لازم نیست.|العب مع فريقك على نفس الواي فاي أو نقطة الاتصال. لا حاجة للإنترنت.|Juega con tu equipo en el mismo Wi-Fi o punto de acceso. Sin internet.
v1.0  -  LAN multiplayer  -  no player limit|v1.0  -  چند نفره محلی  -  بدون محدودیت بازیکن|v1.0  -  لعب جماعي محلي  -  بلا حد للاعبين|v1.0  -  multijugador LAN  -  sin límite de jugadores
FOLLOW AGENT WARFARE|AGENT WARFARE را دنبال کنید|تابع AGENT WARFARE|SIGUE A AGENT WARFARE
PLAYER|بازیکن|اللاعب|JUGADOR
the link is coming soon|لینک به زودی می آید|الرابط قريبا|el enlace llegará pronto
PLAY ON WI-FI / HOTSPOT|بازی با وای فای / هات اسپات|اللعب عبر الواي فاي / نقطة الاتصال|JUGAR POR WI-FI / PUNTO DE ACCESO
All phones must be on the same Wi-Fi - or one phone turns on its hotspot and the others connect to it.|همه گوشی ها باید روی یک وای فای باشند - یا یک گوشی هات اسپات را روشن کند و بقیه به آن وصل شوند.|يجب أن تكون كل الهواتف على نفس الواي فاي - أو يشغل هاتف نقطة الاتصال وتتصل به الهواتف الأخرى.|Todos los teléfonos en el mismo Wi-Fi, o uno activa su punto de acceso y los demás se conectan.
CREATE GROUP|ساخت گروه|إنشاء مجموعة|CREAR GRUPO
Host a match on this phone.\nChoose the mode, map and time,\nthen start when everyone has joined.|میزبان بازی روی این گوشی باش.\nحالت و نقشه و زمان را انتخاب کن\nو وقتی همه آمدند شروع کن.|استضف مباراة على هذا الهاتف.\nاختر الوضع والخريطة والوقت\nثم ابدأ عندما ينضم الجميع.|Organiza una partida en este teléfono.\nElige el modo, el mapa y la hora,\ny empieza cuando todos se unan.
JOIN GROUP|پیوستن به گروه|الانضمام إلى مجموعة|UNIRSE A UN GRUPO
Find the groups hosted nearby\nand join with one tap - or type the\nhost's IP address.|گروه های نزدیک را پیدا کن\nو با یک لمس بپیوند - یا آدرس IP\nمیزبان را بنویس.|ابحث عن المجموعات القريبة\nوانضم بلمسة واحدة - أو اكتب\nعنوان IP للمضيف.|Encuentra los grupos cercanos\ny únete con un toque, o escribe\nla IP del anfitrión.
BACK|بازگشت|رجوع|ATRÁS
GROUP NAME|نام گروه|اسم المجموعة|NOMBRE DEL GRUPO
Group name|نام گروه|اسم المجموعة|Nombre del grupo
PASSWORD|رمز|كلمة السر|CONTRASEÑA
Optional - empty = open|اختیاری - خالی یعنی باز|اختياري - فارغ يعني مفتوح|Opcional: vacío = abierto
TEAM NUMBER|شماره تیم|رقم الفريق|EQUIPO Nº
MAP|نقشه|الخريطة|MAPA
TIME OF DAY|زمان روز|وقت اليوم|HORA DEL DÍA
MATCH TIME|مدت بازی|مدة المباراة|DURACIÓN
MIN|دقیقه|دقيقة|MIN
BOTS|ربات ها|الروبوتات|BOTS
BOT SKILL|مهارت ربات ها|مهارة الروبوتات|NIVEL BOTS
START PRACTICE|شروع تمرین|بدء التدريب|EMPEZAR PRÁCTICA
NONE|هیچ|لا شيء|NINGUNO
STEEL CITY|شهر فولاد|مدينة الفولاذ|CIUDAD DE ACERO
AL-ZAHRA|الزهرا|الزهراء|AL-ZAHRA
VILLAGE|روستا|القرية|ALDEA
OLD TOWN|شهر قدیم|المدينة القديمة|CIUDAD VIEJA
DAY|روز|نهار|DÍA
AFTERNOON|عصر|عصر|TARDE
EVENING|غروب|مساء|ANOCHECER
SUNSET|غروب|غروب|ATARDECER
NIGHT|شب|ليل|NOCHE
EASY|آسان|سهل|FÁCIL
NORMAL|معمولی|عادي|NORMAL
HARD|سخت|صعب|DIFÍCIL
TEAMS|تیم ها|فرق|EQUIPOS
SOLO - NO TEAMS|تنها - بدون تیم|فردي - بلا فرق|SOLO - SIN EQUIPOS
ALONE|تنها|وحيد|SOLO
TEAM|تیم|الفريق|EQUIPO
TEAM {0}|تیم {0}|الفريق {0}|EQUIPO {0}
OR TYPE THE HOST IP:|یا IP میزبان را بنویس:|أو اكتب IP المضيف:|O ESCRIBE LA IP DEL ANFITRIÓN:
CONNECT|اتصال|اتصال|CONECTAR
Type an address like 192.168.43.1|آدرسی مثل 192.168.43.1 بنویس|اكتب عنوانا مثل 192.168.43.1|Escribe una dirección como 192.168.43.1
JOIN|پیوستن|انضمام|UNIRSE
GROUP PASSWORD|رمز گروه|كلمة سر المجموعة|CONTRASEÑA DEL GRUPO
Type the password|رمز را بنویس|اكتب كلمة السر|Escribe la contraseña
Searching for groups on this Wi-Fi / hotspot ...\n\nAsk a friend to tap CREATE GROUP.|در حال جستجوی گروه ها روی این وای فای / هات اسپات ...\n\nاز یک دوست بخواه ساخت گروه را بزند.|جار البحث عن مجموعات على هذا الواي فاي / نقطة الاتصال ...\n\nاطلب من صديق أن يضغط إنشاء مجموعة.|Buscando grupos en este Wi-Fi / punto de acceso ...\n\nPide a un amigo que toque CREAR GRUPO.
{0} PLAYERS|{0} بازیکن|{0} لاعبين|{0} JUGADORES
IN MATCH|در بازی|في مباراة|EN PARTIDA
PLAYERS|بازیکنان|اللاعبون|JUGADORES
LOBBY|لابی|الردهة|SALA
JOINING ...|در حال پیوستن ...|جار الانضمام ...|UNIÉNDOSE ...
PRACTICE|تمرین|تدريب|PRÁCTICA
EVERYONE ALONE|همه تنها|الجميع فرادى|TODOS SOLOS
Only you and the bots - no network used|فقط تو و ربات ها - بدون شبکه|أنت والروبوتات فقط - بلا شبكة|Solo tú y los bots: sin red
Friends: JOIN GROUP, or type this IP|دوستان: پیوستن به گروه - یا این IP را بنویسند|الأصدقاء: الانضمام إلى مجموعة - أو اكتبوا هذا العنوان IP|Amigos: UNIRSE A UN GRUPO, o escribid esta IP
(no Wi-Fi found)|(وای فای پیدا نشد)|(لا يوجد واي فاي)|(sin Wi-Fi)
Connected - {0} players in the group|وصل شد - {0} بازیکن در گروه|متصل - {0} لاعبين في المجموعة|Conectado: {0} jugadores en el grupo
PRIMARY|اسلحه اول|السلاح الأول|PRINCIPAL
SECOND|اسلحه دوم|السلاح الثاني|SECUNDARIA
LEAVE|ترک|مغادرة|SALIR
START MATCH|شروع بازی|بدء المباراة|EMPEZAR PARTIDA
Waiting for the host to start ...|منتظر شروع میزبان ...|بانتظار أن يبدأ المضيف ...|Esperando a que empiece el anfitrión ...
Match in progress - joining ...|بازی در جریان است - در حال پیوستن ...|المباراة جارية - جار الانضمام ...|Partida en curso: uniéndose ...
TEAM BLUE|تیم آبی|الفريق الأزرق|EQUIPO AZUL
TEAM RED|تیم قرمز|الفريق الأحمر|EQUIPO ROJO
GRAPHICS|گرافیک|الرسوميات|GRÁFICOS
RESOLUTION|وضوح تصویر|الدقة|RESOLUCIÓN
FRAME RATE|نرخ فریم|معدل الإطارات|FOTOGRAMAS
SOUND VOLUME|صدا|مستوى الصوت|VOLUMEN
SHOW FPS|نمایش FPS|إظهار FPS|MOSTRAR FPS
AUTO GRAPHICS|گرافیک خودکار|رسوميات تلقائية|GRÁFICOS AUTO
TURN SPEED|سرعت چرخش|سرعة الدوران|GIRO
AIM SPEED|سرعت نشانه روی|سرعة التصويب|APUNTANDO
SCOPE SPEED|سرعت دوربین|سرعة المنظار|CON MIRA
LEFT / RIGHT|چپ / راست|يسار / يمين|IZQ. / DER.
UP / DOWN|بالا / پایین|أعلى / أسفل|ARRIBA / ABAJO
AIM ASSIST|کمک نشانه روی|مساعدة التصويب|AYUDA PUNTERÍA
MUSIC|موسیقی|الموسيقى|MÚSICA
LANGUAGE|زبان|اللغة|IDIOMA
ON|روشن|تشغيل|SÍ
OFF|خاموش|إيقاف|NO
REVERSED|برعکس|معكوس|INVERTIDO
LOW|پایین|منخفض|BAJA
MEDIUM|متوسط|متوسط|MEDIA
HIGH|بالا|عال|ALTA
Running speed is the same for everyone and cannot be changed.|سرعت دویدن برای همه یکسان است و تغییر نمی کند.|سرعة الجري واحدة للجميع ولا يمكن تغييرها.|La velocidad de carrera es igual para todos y no se puede cambiar.
CONTROLS LAYOUT|چیدمان دکمه ها|ترتيب الأزرار|MOVER BOTONES
PAUSED|توقف|متوقف|PAUSA
RESUME|ادامه|استئناف|CONTINUAR
LEAVE MATCH|ترک بازی|مغادرة المباراة|SALIR DE LA PARTIDA
MATCH OVER|بازی تمام شد|انتهت المباراة|FIN DE LA PARTIDA
LEAVE GROUP|ترک گروه|مغادرة المجموعة|SALIR DEL GRUPO
CLOSE|بستن|إغلاق|CERRAR
BACK TO LOBBY|بازگشت به لابی|العودة إلى الردهة|VOLVER A LA SALA
DRAW|مساوی|تعادل|EMPATE
WINS|برنده شد|يفوز|GANA
VICTORY|پیروزی|نصر|VICTORIA
DEFEAT|شکست|هزيمة|DERROTA
YOU WIN!|تو بردی!|لقد فزت!|¡GANASTE!
RATING = score next to the best player   -   the group goes back to the lobby in {0}s|رتبه = امتیاز نسبت به بهترین بازیکن   -   گروه تا {0} ثانیه دیگر به لابی برمی گردد|التقييم = النقاط مقارنة بأفضل لاعب   -   تعود المجموعة إلى الردهة خلال {0} ثانية|VALORACIÓN = puntos frente al mejor jugador   -   el grupo vuelve a la sala en {0} s
RATING = score next to the best player   -   the group is in the lobby: close the table when you are ready|رتبه = امتیاز نسبت به بهترین بازیکن   -   گروه در لابی است: هر وقت آماده بودی جدول را ببند|التقييم = النقاط مقارنة بأفضل لاعب   -   المجموعة في الردهة: أغلق الجدول عندما تكون جاهزا|VALORACIÓN = puntos frente al mejor jugador   -   el grupo está en la sala: cierra la tabla cuando estés listo
KILLS|کشته|القتلى|BAJAS
DEATHS|مرگ|الموت|MUERTES
HITS|اصابت|الإصابات|IMPACTOS
DAMAGE|آسیب|الضرر|DAÑO
HEAD|سر|الرأس|CABEZA
ACCURACY|دقت|الدقة|PRECISIÓN
SCORE|امتیاز|النقاط|PUNTOS
RATING|رتبه|التقييم|VALORACIÓN
BOT|ربات|روبوت|BOT
BY TEAM|به تفکیک تیم|حسب الفريق|POR EQUIPO
BY PLAYER|به تفکیک بازیکن|حسب اللاعب|POR JUGADOR
MATCH ENDS IN|پایان بازی|تنتهي خلال|TERMINA EN
RANK|رتبه|الترتيب|RANGO
STOP|توقف|إيقاف|PARAR
FIRE|شلیک|إطلاق|DISPARO
FIRE 2|شلیک 2|إطلاق 2|DISPARO 2
AIM|نشانه|تصويب|APUNTAR
JUMP|پرش|قفز|SALTAR
CROUCH|نشستن|انحناء|AGACHARSE
RELOAD|پرکردن|تعبئة|RECARGAR
SWAP|تعویض|تبديل|CAMBIAR
PRONE|درازکش|انبطاح|TUMBARSE
AMMO|مهمات|ذخيرة|MUNICIÓN
BLOOD|خون|دم|SANGRE
GRENADE|نارنجک|قنبلة|GRANADA
HEADSHOT!|شلیک به سر!|في الرأس!|¡A LA CABEZA!
ELIMINATED|حذف شد|تم القضاء عليه|ELIMINADO
GET READY|آماده شو|استعد|PREPÁRATE
YOU WERE ELIMINATED|تو حذف شدی|تم القضاء عليك|TE ELIMINARON
Killed by {0}|کشته شده توسط {0}|قتلك {0}|Te eliminó {0}
killed|کشت|قتل|eliminó
eliminated|حذف شد|قضي عليه|eliminado
SOLO|تنها|فردي|SOLO
respawning...|بازگشت به میدان...|العودة إلى الميدان...|reapareciendo...
TOP {0}|برترین {0}|الأعلى {0}|MEJOR {0}
AMMO AND GRENADES REFILLED|مهمات و نارنجک ها پر شد|تمت تعبئة الذخيرة والقنابل|MUNICIÓN Y GRANADAS RECARGADAS
DO NOT SHOOT THE BOARDS|به تابلوها شلیک نکن|لا تطلق النار على اللوحات|NO DISPARES A LOS LETREROS
the shot comes back on you|گلوله به خودت برمی گردد|الطلقة ترتد عليك|el disparo se vuelve contra ti
TOO FAR AWAY|خیلی دور است|بعيد جدا|DEMASIADO LEJOS
{0} GAVE YOU BLOOD  +{1}|{0} به تو خون داد  +{1}|{0} أعطاك دما  +{1}|{0} TE DIO SANGRE  +{1}
YOU GAVE {0} BLOOD  +{1}|تو به {0} خون دادی  +{1}|أعطيت {0} دما  +{1}|DISTE SANGRE A {0}  +{1}
GRAPHICS LOWERED FOR SMOOTH PLAY ({0} FPS)|گرافیک برای بازی روان پایین آمد ({0} FPS)|خفضت الرسوميات للعب سلس ({0} FPS)|GRÁFICOS REDUCIDOS PARA JUGAR FLUIDO ({0} FPS)
Could not reach the group. Check that you are on the same Wi-Fi or hotspot.|به گروه وصل نشد. ببین روی همان وای فای یا هات اسپات هستی.|تعذر الوصول إلى المجموعة. تأكد أنك على نفس الواي فاي أو نقطة الاتصال.|No se pudo llegar al grupo. Comprueba que estás en el mismo Wi-Fi o punto de acceso.
The connection to the host was lost|اتصال با میزبان قطع شد|انقطع الاتصال بالمضيف|Se perdió la conexión con el anfitrión
The group is full|گروه پر است|المجموعة ممتلئة|El grupo está lleno
The host closed the group|میزبان گروه را بست|أغلق المضيف المجموعة|El anfitrión cerró el grupo
The host has a different game version|نسخه بازی میزبان فرق دارد|لدى المضيف نسخة مختلفة من اللعبة|El anfitrión tiene otra versión del juego
This group is locked: type its password|این گروه قفل است: رمزش را بنویس|هذه المجموعة مقفلة: اكتب كلمة السر|Este grupo está cerrado: escribe su contraseña
Wrong password for this group|رمز این گروه اشتباه است|كلمة سر خاطئة لهذه المجموعة|Contraseña incorrecta para este grupo
You were removed from the group|از گروه بیرون شدی|تمت إزالتك من المجموعة|Te expulsaron del grupo
Could not open the network port|درگاه شبکه باز نشد|تعذر فتح منفذ الشبكة|No se pudo abrir el puerto de red
Controls layout saved|چیدمان دکمه ها ذخیره شد|تم حفظ ترتيب الأزرار|Disposición guardada
Checking ...|در حال بررسی ...|جار التحقق ...|Comprobando ...
Drag a button to move it.  Select it and use  -  /  +  to resize,  HIDE to remove it.|دکمه را بکش تا جابجا شود.  انتخابش کن و با  -  /  +  اندازه اش را عوض کن  -  پنهان برای برداشتن.|اسحب الزر لتحريكه.  اختره واستخدم  -  /  +  لتغيير الحجم  -  إخفاء لإزالته.|Arrastra un botón para moverlo.  Selecciónalo y usa  -  /  +  para cambiar su tamaño,  OCULTAR para quitarlo.
HIDE|پنهان|إخفاء|OCULTAR
SAVE|ذخیره|حفظ|GUARDAR
RESET|بازنشانی|إعادة ضبط|RESTABLECER
NAME|نام|الاسم|NOMBRE
Your name|نام تو|اسمك|Tu nombre
CHARACTER|شخصیت|الشخصية|PERSONAJE
WEAPONS|سلاح ها|الأسلحة|ARMAS
STYLE|سبک|النمط|ESTILO
drag to turn|برای چرخاندن بکشید|اسحب للتدوير|arrastra para girar
AS WEAPON 1|به عنوان سلاح 1|كسلاح 1|COMO ARMA 1
AS WEAPON 2|به عنوان سلاح 2|كسلاح 2|COMO ARMA 2
POWER|قدرت|القوة|POTENCIA
FIRE RATE|سرعت شلیک|معدل الإطلاق|CADENCIA
RANGE|برد|المدى|ALCANCE
CONTROL|کنترل|التحكم|CONTROL
MOBILITY|تحرک|الحركة|MOVILIDAD
MAGAZINE|خشاب|المخزن|CARGADOR
LOW RECOIL|لگد کم|ارتداد منخفض|RETROCESO BAJO
MEDIUM RECOIL|لگد متوسط|ارتداد متوسط|RETROCESO MEDIO
HEAVY RECOIL|لگد زیاد|ارتداد قوي|RETROCESO ALTO
PIN-POINT|بسیار دقیق|دقيق جدا|MÁXIMA
RIFLE CLASS|رده تفنگ|فئة البنادق|CLASE FUSIL
SIDEARM CLASS|رده کمری|فئة المسدسات|CLASE PISTOLA
WELCOME, AGENT|خوش آمدی مامور|أهلا بك أيها العميل|BIENVENIDO, AGENTE
What should the others call you?|دیگران تو را چه صدا کنند?|بماذا يناديك الآخرون?|¿Cómo te llamarán los demás?
SKIP|رد شدن|تخطي|OMITIR
THIS VERSION IS NOT ACTIVE|این نسخه فعال نیست|هذه النسخة غير مفعلة|ESTA VERSIÓN NO ESTÁ ACTIVA
CHECK AGAIN|دوباره بررسی کن|تحقق مجددا|COMPROBAR DE NUEVO
FIRST BLOOD|اولین کشته|أول قتيل|PRIMERA SANGRE
AGENT|مامور|العميل|AGENTE
TOP|بالاتنه|الجزء العلوي|PARTE SUPERIOR
BOTTOM|پایین تنه|الجزء السفلي|PARTE INFERIOR
VEST|جلیقه|السترة|CHALECO
SHOES|کفش|الحذاء|CALZADO
GLOVES|دستکش|القفازات|GUANTES
BELT|کمربند|الحزام|CINTURÓN
BEARD|ریش|اللحية|BARBA
BAG|کوله|الحقيبة|MOCHILA
TOP COLOUR|رنگ بالاتنه|لون الجزء العلوي|COLOR ARRIBA
BOTTOM COLOUR|رنگ پایین تنه|لون الجزء السفلي|COLOR ABAJO
HEAD COLOUR|رنگ سرپوش|لون غطاء الرأس|COLOR DE CABEZA
MAN - LIGHT|مرد - روشن|رجل - فاتح|HOMBRE - CLARO
MAN - DARK|مرد - تیره|رجل - داكن|HOMBRE - OSCURO
COMBAT JACKET|کاپشن رزمی|سترة قتالية|CHAQUETA DE COMBATE
AFGHAN PERAHAN|پیراهن افغانی|قميص أفغاني|PERAHAN AFGANO
ARAB THOBE|ثوب عربی|ثوب عربي|ZOB ÁRABE
FORMAL SUIT|کت و شلوار|بدلة رسمية|TRAJE FORMAL
LONG SHIRT|پیراهن بلند|قميص طويل|CAMISA LARGA
AFGHAN CHAPAN|چپن افغانی|تشابان أفغاني|CHAPAN AFGANO
ABAYA|عبا|عباءة|ABAYA
LONG COAT|کت بلند|معطف طويل|ABRIGO LARGO
CARGO TROUSERS|شلوار کارگو|بنطال كارغو|PANTALÓN CARGO
AFGHAN TUNBAN|تنبان افغانی|تنبان أفغاني|TUNBAN AFGANO
SUIT TROUSERS|شلوار رسمی|بنطال رسمي|PANTALÓN DE TRAJE
JEANS|جین|جينز|VAQUEROS
COMBAT HELMET|کلاه خود رزمی|خوذة قتالية|CASCO DE COMBATE
HELMET + HEADSET|کلاه خود + هدست|خوذة + سماعة|CASCO + AURICULARES
HELMET + NIGHT VISION|کلاه خود + دید در شب|خوذة + رؤية ليلية|CASCO + VISIÓN NOCTURNA
PAKOL|پکول|باكول|PAKOL
TURBAN|دستار|عمامة|TURBANTE
GHUTRA + AGAL|چفیه + عقال|غترة + عقال|GUTRA + AGAL
SHEMAGH + AGAL|شماغ + عقال|شماغ + عقال|SHEMAGH + AGAL
HIJAB|حجاب|حجاب|HIYAB
PRAYER CAP|کلاه نماز|طاقية|GORRO DE ORACIÓN
BERET|کلاه بره|بيريه|BOINA
BALACLAVA|نقاب|قناع|PASAMONTAÑAS
FIELD CAP|کلاه کپی|قبعة ميدانية|GORRA DE CAMPAÑA
HOOD + SCARF|کلاه و شال|غطاء رأس ووشاح|CAPUCHA Y BUFANDA
CHEST RIG|جلیقه مهمات|حمالة ذخيرة|PORTACARGADORES
PLATE CARRIER|جلیقه ضد گلوله|سترة واقية|PORTAPLACAS
WAISTCOAT|جلیقه|صدرية|CHALECO
COMBAT BOOTS|پوتین رزمی|حذاء قتالي|BOTAS DE COMBATE
DESERT BOOTS|پوتین صحرایی|حذاء صحراوي|BOTAS DE DESIERTO
FORMAL SHOES|کفش رسمی|حذاء رسمي|ZAPATOS FORMALES
SANDALS|صندل|صندل|SANDALIAS
TACTICAL GLOVES|دستکش تاکتیکی|قفازات تكتيكية|GUANTES TÁCTICOS
BELT + KNEE PADS|کمربند + زانوبند|حزام + واقيات ركبة|CINTURÓN + RODILLERAS
ASSAULT PACK|کوله پشتی|حقيبة هجومية|MOCHILA DE ASALTO
OWN BUILD|ساخت خودم|من تصميمي|PROPIO
MERCENARY|مزدور|مرتزق|MERCENARIO
POLICE OFFICER|پلیس|شرطي|POLICÍA
FIELD AGENT|مامور میدانی|عميل ميداني|AGENTE DE CAMPO
OPERATIVE|عملیاتی|عنصر عمليات|OPERATIVO
BODYGUARD|محافظ|حارس شخصي|GUARDAESPALDAS
SWAT|نیروی واکنش سریع|قوات التدخل|SWAT
RIFLEMAN|تفنگدار|رامي بندقية|FUSILERO
RED BERET|کلاه سرخ|القبعة الحمراء|BOINA ROJA
SPECIAL FORCES|نیروی ویژه|القوات الخاصة|FUERZAS ESPECIALES
SOLDIER|سرباز|جندي|SOLDADO
DESERT SCOUT|دیده بان صحرا|كشاف الصحراء|EXPLORADOR DEL DESIERTO
CIVILIAN|غیرنظامی|مدني|CIVIL
AFGHAN ELDER|ریش سفید افغان|شيخ أفغاني|ANCIANO AFGANO
ARAB|عرب|عربي|ÁRABE
STATESMAN|دولتمرد|رجل دولة|ESTADISTA
EVERYDAY|روزمره|يومي|COTIDIANO
AFGHAN GUARD|نگهبان افغان|حارس أفغاني|GUARDIA AFGANO
GUARD|نگهبان|حارس|GUARDIA
BLACK TURBAN|دستار سیاه|عمامة سوداء|TURBANTE NEGRO
TEAM 1|تیم 1|الفريق 1|EQUIPO 1
URBAN|شهری|حضري|URBANO
WOODLAND|جنگلی|غابي|BOSCOSO
DESERT|صحرایی|صحراوي|DESIERTO
ELDER|ریش سفید|شيخ|ANCIANO
AFGHAN|افغان|أفغاني|AFGANO
URBAN CAMO|استتار شهری|تمويه حضري|CAMUFLAJE URBANO
DESERT CAMO|استتار صحرایی|تمويه صحراوي|CAMUFLAJE DESÉRTICO
WOODLAND CAMO|استتار جنگلی|تمويه غابي|CAMUFLAJE BOSCOSO
BLACK CAMO|استتار سیاه|تمويه أسود|CAMUFLAJE NEGRO
OLIVE CAMO|استتار زیتونی|تمويه زيتوني|CAMUFLAJE OLIVA
SAND CAMO|استتار شنی|تمويه رملي|CAMUFLAJE ARENA
NAVY CAMO|استتار سرمه ای|تمويه كحلي|CAMUFLAJE MARINO
GREY CAMO|استتار خاکستری|تمويه رمادي|CAMUFLAJE GRIS
HEX CAMO|استتار شش ضلعی|تمويه سداسي|CAMUFLAJE HEXAGONAL
BEIGE|بژ|بيج|BEIGE
BLACK|سیاه|أسود|NEGRO
BLUE|آبی|أزرق|AZUL
BROWN|قهوه ای|بني|MARRÓN
BURGUNDY|زرشکی|عنابي|BURDEOS
CAMEL|شتری|جملي|CAMELLO
CHARCOAL|زغالی|فحمي|CARBÓN
CREAM|کرم|كريمي|CREMA
GREEN|سبز|أخضر|VERDE
GREY|خاکستری|رمادي|GRIS
INDIGO|نیلی|نيلي|ÍNDIGO
KHAKI|خاکی|كاكي|CAQUI
LIGHT GREY|خاکستری روشن|رمادي فاتح|GRIS CLARO
MAROON|عنابی|كستنائي|GRANATE
NATURAL|طبیعی|طبيعي|NATURAL
NAVY|سرمه ای|كحلي|AZUL MARINO
OLIVE|زیتونی|زيتوني|OLIVA
ORANGE|نارنجی|برتقالي|NARANJA
SAND|شنی|رملي|ARENA
SKY BLUE|آبی آسمانی|أزرق سماوي|AZUL CIELO
STONE WASH|سنگ شور|مغسول بالحجر|LAVADO A LA PIEDRA
TEAL|سبز آبی|أزرق مخضر|VERDE AZULADO
WHITE|سفید|أبيض|BLANCO
BLACK STRIPE|راه راه سیاه|مقلم أسود|RAYAS NEGRAS
BLUE STRIPE|راه راه آبی|مقلم أزرق|RAYAS AZULES
BROWN STRIPE|راه راه قهوه ای|مقلم بني|RAYAS MARRONES
GOLD STRIPE|راه راه طلایی|مقلم ذهبي|RAYAS DORADAS
GREEN STRIPE|راه راه سبز|مقلم أخضر|RAYAS VERDES
GREY STRIPE|راه راه خاکستری|مقلم رمادي|RAYAS GRISES
PURPLE STRIPE|راه راه بنفش|مقلم بنفسجي|RAYAS MORADAS
RED STRIPE|راه راه قرمز|مقلم أحمر|RAYAS ROJAS
";
    }
}
