# Osu! Random Beatmap

The program gives you random beatmaps for the game osu! by generating random numbers. It is not skill-based and it does not do any kind
of calculation: beatmaps found are just totally random. Since it is very simple to create a program like that, we decided to make it at least
good-looking and with many useful function, such as an embedded downloader (ORB!direct). The mirror used for beatmap downloader is [Bloodcat](https://bloodcat.com/osu/).

## Third Party Libraries

- [CefSharp](https://github.com/cefsharp/CefSharp)
- [Json.NET](https://github.com/JamesNK/Newtonsoft.Json)
- [osu-database-reader](https://github.com/HoLLy-HaCKeR/osu-database-reader)
- [ProgressBar.js](https://github.com/kimmobrunfeldt/progressbar.js)
- [NAudio](https://github.com/naudio/NAudio)
- [JQuery](https://github.com/kimmobrunfeldt/progressbar.js)

## To Do

- Convert all Javascript functions to JQuery.

## Warning

ORB back-end is written in C#, while the front-end uses HTML, CSS3 and Javascript: you can find the source code in the resources directory.
I did this "obfuscation" in order to not make the program files not editable by any one.
