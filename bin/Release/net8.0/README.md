# ClassLink for ClassIsland

ClassIsland 2.x 插件，通过本机回环 HTTP 接收受支持播放器推送的结构化歌词、播放时间锚点与封面。

- 默认监听 `127.0.0.1:38973`
- 必须使用插件设置页生成的连接令牌
- 支持 SPlayer-Next、CloudMusic CLI 及其他兼容 ClassLink v1 的播放器
- 提供“ClassLink 歌词”和“ClassLink 正在播放”两个主界面组件
- “正在播放”仅显示封面、歌曲名与歌手，适合紧凑布局
- 封面使用与 SPlayer 任务栏歌词一致的 6px 圆角
- 支持逐字歌词、翻译、音译、TTML 背景伴唱和对唱
- 背景伴唱会并入对应主唱行，最多保持两行并使用独立逐字进度
- 切行时旧歌词向上退出，新歌词从下方弹入
- 多个播放器同时运行时使用最近启动的活动源，断开后允许其他发送端自动接管

---

Powered By LaoShui @ 2026
