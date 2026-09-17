# Сетевой протокол Golether (версия 1)

## Транспорт

TCP, порт ведущего по умолчанию **47800**. Каждое соединение:

1. TLS 1.3 (TLS 1.2 на Windows 10) с сертификатами у обеих сторон. Проверка цепочки и имени отключена:
   участник сверяет `PeerId` сертификата ведущего с приглашением, ведущий вычисляет `PeerId` участника.
2. Преамбула клиента, 6 байт: `47 4C 54 48` (`GLTH`), версия `01`, назначение (`01` Control, `02` MediaData).
3. Ответ ведущего, 1 байт: `01` — принято.

`PeerId` = lowercase hex SHA-256 от DER SubjectPublicKeyInfo ключа устройства (ECDSA P-256).

## Control

Кадры: 4 байта длины (big-endian) и UTF-8 JSON, не больше 256 КиБ. Тип задаётся полем `$t`, имена полей в camelCase.

| `$t` | Направление | Поля | Смысл |
|---|---|---|---|
| `hello` | У → В | `version`, `displayName`, `inviteToken` | первое сообщение |
| `pending` | В → У | — | ждём решения ведущего |
| `welcome` | В → У | `sessionName`, `participants[]`, `playback`, `media`, `mediaDataStreams`, `relay` | участник принят; `relay` — порт и одноразовые учётные данные TURN ведущего |
| `rejected` | В → У | `reason`, `message` | отказ, соединение закрывается |
| `ping` | У → В | `clientSendTime` | проба часов (t0, мкс) |
| `pong` | В → У | `clientSendTime`, `hostReceiveTime`, `hostSendTime` | t0, t1, t2 |
| `request` | У → В | `request: {kind, position}` | намерение: `0` play, `1` pause, `2` seek |
| `state` | В → все | `state: PlaybackState` | авторитетное состояние |
| `status` | У → В | `status: ParticipantStatus` | раз в секунду; `peerId` ведущий подменяет на проверенный |
| `participants` | В → все | `participants[]`, `statuses[]` | раз в секунду и при изменениях |
| `media` | В → все | `media: MediaDescriptor` | новый файл |
| `rtc` | любой | `peer`, `kind`, `payload` | сигналинг WebRTC; ведущий пересылает, подставляя проверенного отправителя |
| `moderate` | В → У | `microphone`, `camera` | ведущий выключает устройства участника (только выключение) |
| `bye` | любой | `reason` | выход |

`PlaybackState`: `state` (`0` пауза, `1` воспроизведение), `position` (`hh:mm:ss.fffffff`), `referenceTime`
(мкс по часам ведущего), `rate`, `version`, `origin`, `cause`.

Причины отказа: `0` неверное приглашение, `1` отклонён, `2` ведущий не ответил, `3` сеанс полон,
`4` несовместимая версия, `5` удалён ведущим.

Порядок подключения: `hello` → (`pending`) → `welcome` | `rejected`. Токен из приглашения одноразовый;
доверенные контакты могут подключаться без токена.

## MediaData

Запрос, 17 байт: операция (`01` GetChunk — кусок, `02` GetHash — только хэш), индекс куска (int64 BE), ключ файла
(uint64 BE, первые 8 байт быстрого идентификатора). На GetHash ответ приходит без данных.

Ответ, 45 байт и данные: статус (`0` OK, `1` вне файла, `2` другой файл, `3` ошибка чтения), индекс (int64 BE),
длина (int32 BE), SHA-256 данных (32 байта).

Размер куска по умолчанию 4 МиБ (допустимо 64 КиБ – 16 МиБ). Быстрый идентификатор = SHA-256(длина int64 LE,
первый МиБ, средний МиБ, последний МиБ).

## Обмен кусками между участниками

Идёт по каналу данных WebRTC `golether` (SCTP поверх DTLS; сообщения доступны только после сверки сертификата
собеседника). Все числа big-endian.

| Тип | Содержимое | Смысл |
|---|---|---|
| `1` have | ключ файла (8), число диапазонов (2), диапазоны: начало (4), длина (4) | какие куски есть; раз в 2 с |
| `2` request | ключ файла (8), номер запроса (4), индекс куска (4) | просьба прислать кусок |
| `3` data | номер запроса (4), смещение (4), полный размер (4), данные | часть куска (по 16 КиБ, по порядку) |
| `4` miss | номер запроса (4) | куска нет или отдающий занят (не больше 2 отдач на участника) |

Полученный кусок принимается, только если его SHA-256 совпадает с хэшем от ведущего (GetHash); иначе кусок
запрашивается у ведущего.

## Приглашение

`golether://join/v1/` + base64url(JSON):

```json
{ "host": "<PeerId>", "name": "Вы", "session": "Вечер кино",
  "endpoints": ["192.168.1.10:47800", "[2001:db8::5]:47800"],
  "token": "<22 символа base64url, 128 бит>", "expires": 1789000000 }
```

## Пакеты AmneziaWG

`golether-awg:<offer|answer>:v1:<base64url(JSON тела)>.<base64url(подписи)>`.
Подпись: ECDSA P-256/SHA-256 (IEEE P1363) по ASCII-строке до точки. Пробелы и переводы строк внутри пакета
игнорируются, так что пакет можно пересылать с переносами.

Тело предложения: `offerId`, `hostName`, `hostCertificate` (DER, base64), `hostPublicKey` (AWG), `hostEndpoints[]`,
`hostAddress`, `assignedAddress`, `s1`, `s2`, `headers[4]`, `ephemeralKey` (SPKI P-256, base64), `expiresAt`.

Тело ответа: `offerId`, `participantName`, `participantCertificate`, `participantPublicKey`,
`participantEndpoints[]`, `ephemeralKey`.

PSK = HKDF-SHA256(IKM = ECDH(эфемерные ключи), длина 32, соль = ASCII(offerId), info = `golether-awg-psk-v1`).

Код сверки = `VerificationCode(hostPeerId, participantPeerId, "awg:" + offerId)`.
