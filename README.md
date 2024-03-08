# Telegram группа
https://t.me/+TIXtgvGBBOc3ZTUy

# Запуск в Docker с возможностью редактирования файла init.conf
```
docker run -d -p 9118:9118 \
-v /local_folder/init.conf:/home/init.conf \
--restart always --name lampac immisterio/lampac 
```

# Установка на linux
спасибо @nikk, @Denis
```
curl -s https://raw.githubusercontent.com/immisterio/lampac/main/install.sh | bash
```

# Установка на Windows
1. Установить ".NET Core 6" https://learn.microsoft.com/ru-ru/dotnet/core/install/
2. Загрузить и распаковать релиз https://github.com/immisterio/lampac/releases
3. Запустить Lampac.exe

# Запуск в Android
1. BwaJS - https://bwa.to
2. Termux - https://github.com/bbk14/TermuxDebian/blob/main/README.md

# Плагины для Lampa
1. онлайн   - "http://IP:9118/online.js"
2. xxx      - "http://IP:9118/sisi.js"
3. DLNA     - "http://IP:9118/dlna.js"
4. TimeCode - "http://IP:9118/timecode.js"
5. Tracks   - "http://IP:9118/tracks.js"
6. TorrServer      - "http://IP:9118/ts.js"
7. Парсер Jackett  - "IP:9118"

# Плагины для Lampa Lite
1. онлайн/jackett  - "http://IP:9118/lite.js" 
2. xxx     - "http://IP:9118/sisi.js"

# Общие настройки
1. Отключить TorrServer/DNLA/Jackett/etc можно в module/manifest.json
2. Настройки Jackett в module/JacRed.conf (пример JacRed.example.conf)

# Источники 
* Filmix, Kinobase, Rezka, Voidboost, VideoCDN, VideoDB, Collaps, HDVB, Zetflix (VideoDB), Kodik, Ashdi (UKR), Eneyida (UKR), Kinotochka, Kinoprofi, LostfilmHD, IframeVideo, CDNmovies, Anilibria, AniMedia, AnimeGo, Animevost, Animebesst, Redheadsound, Alloha, Seasonvar, KinoPub, VoKino
* Kinozal, Nnmclub, Rutor, Megapeer, Torrentby, Bitru, Anilibria, Toloka (UKR), Rutracker, Selezen, LostFilm, Animelayer, Anifilm
* PornHub, PornHubPremium, Bongacams, Chaturbate, Ebalovo, Eporner, HQporner, Porntrex, Spankbang, Xhamster, Xnxx, Xvideos

# Привязка PRO аккаунтов
* Filmix - "http://IP:9118/lite/filmixpro" 
* KinoPub - "http://IP:9118/lite/kinopubpro" 
* VoKino - "http://IP:9118/lite/vokinotk" 

# Плагин DLNA.js
* Просмотр медиа файлов с папки dlna
* Возможность удалять просмотренные папки/файлы
* Загрузка торрентов в папку dlna

Зажмите кнопку "OK" на выбранном торренте/папке/файле для вызова списка действий

# Плагин Timecode.js
Синхронизация отметок просмотра между разными устройствами

# Плагин Tracks.js
Заменяет название аудиодорожек и субтитров в плеере

Автор: @aabytt

1. Добавить плагин "http://IP:9118/tracks.js" 
2. В init.conf заменить значение "ffprobe.os" на один из вариантов "win", "linux"


# Плагин TmdbProxy.js
Проксирование постеров для сайта TMDB

1. Добавить плагин "http://IP:9118/tmdbproxy.js" 
2. В настройках TMDB включить проксирование

# Доступ к доменам .onion
1. Запустить tor на порту 9050
2. В init.conf указать .onion домен в host

# Media Station X
1. Settings -> Start Parameter -> Setup
2. Enter current ip address and port "IP:9118"

Убрать/Добавить адреса можно в msx.json

# Виджеты
1. Для Samsung "IP:9118/samsung.wgt"

# Параметры init.conf
* xdb - Выводит платные источники с sisi.am
* fileCacheInactiveDay - Время хранения резервного кеша на диске
* checkOnlineSearch - Делать предварительный поиск скрывая балансеры без ответа
* multiaccess - Настройка кеша в онлайн с учетом многопользовательского доступа
* accsdb - Доступ к API через авторизацию (для jackett используется apikey)
* useproxy - Парсит источник через прокси указанные в "proxy"
* streamproxy - Перенаправляет видео через "http://IP:9118/proxy/{uri}" 
* disableserverproxy - Запрещает запросы через "http://IP:9118/(proxy|proxyimg)/"
* localip - Заменить на "false" если скрипт установлен за пределами внутренней сети
* proxytoproxyimg - Использовать прокси при получении картинки в "http://IP:9118/proxyimg/"
* SisiHeightPicture - Уменьшение размера картинки в xxx по высоте до 200px
* findkp - Каталог для поиск kinopoisk_id (alloha|tabus|vsdn)
* corseu - Использовать прокси cors.bwa.workers.dev

# Настройки при совместном использовании
* multiaccess - true

В Lampac.runtimeconfig.json убрать или увеличить лимит потребления памяти 
```
"System.GC.HeapCount": 30,
"System.GC.HeapHardLimit": 309715200, // ~300mb
```

# Пример init.conf
* Список всех параметров, а так же значения по умолчанию смотреть в example.conf 
* В init.conf нужно указывать только те параметры, которые хотите изменить

```
{
  "listenport": 9120, // изменили порт
  "jac": {
    "cachetype": "mem", // изменили место хранения кеша
    "apikey": "1"       // запретили доступ без ключа авторизации
  },
  "dlna": {
    "downloadSpeed": 25000000 // ограничили скорость загрузки до 200 Mbit/s
  },
  "sisi": {
    "xdb": true // вывели доп. источники с sisi.am
  },
  "Rezka": {
    "streamproxy": true // отправили видеопоток через "http://IP:9118/proxy/{uri}" 
  },
  "Filmix": {
    "token": "protoken" // добавили токен от PRO аккаунта
  },
  "PornHub": {
    "enable": false // отключили PornHub
  },
  "globalproxy": [
    {
      "pattern": "\\.onion",  // запросы на домены .onion отправить через прокси
      "list": [
        "socks5://127.0.0.1:9050" // прокси сервер tor
      ]
    }
  ],
  "overrideResponse": [ // Заменили ответ на данные из файла myfile.json
    {
      "pattern": "/msx/start.json",
      "action": "file",
      "type": "application/json; charset=utf-8",
      "val": "myfile.json"
    }
  ]
}
```
