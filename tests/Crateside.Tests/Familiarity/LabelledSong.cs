namespace Crateside.Tests.Familiarity;

/// <summary>A song the maintainer labelled known or not, with the numbers the note of 2026-11-09 measured for it.</summary>
public sealed record LabelledSong(
    string Artist,
    string Title,
    bool Known,
    int? Listeners,
    int? Pageviews
)
{
    /// <summary>The 52 songs of docs/notes/2026-11-09-what-tells-a-known-song-from-an-unknown-one.md.</summary>
    public static IReadOnlyList<LabelledSong> All { get; } =
    [
        new("1991", "if only", false, 254, null),
        new("2 unlimited", "get ready", true, 854, null),
        new("aiobahn feat. ナナヲアカリ", "しあわせになんてならないで", false, 433, null),
        new("andrew dum, alina eremia, café del mundo", "paradise (radio edit)", false, null, null),
        new("angel mina", "turn me up until i'm gone", false, 10, null),
        new("basement jaxx", "never say never (extended version)", false, 674, 1719),
        new("basement jaxx, lisa kekaula", "good luck", false, 39037, 6736),
        new("belinda carlisle", "heaven is a place on earth", true, 51850, 119681),
        new("biz markie", "just a friend", true, 33304, 38552),
        new("black box", "everybody everybody", true, 6921, 22475),
        new("bloodhound gang", "the bad touch", true, 18233, 85697),
        new("bodyrox, luciana", "yeah yeah (d ramirez vocal radio edit)", false, 8505, 6676),
        new("bomfunk mc's", "freestyler", false, 48401, 76640),
        new("bow wow wow", "i want candy", true, 31200, 73885),
        new("chingy", "right thurr", true, 18899, 21329),
        new("clozee, barbatuques", "baiana (clozee remix)", false, null, null),
        new("craig mack", "flava in ya ear", true, 8515, 31156),
        new(
            "crystal waters",
            "gypsy woman (she's homeless) (la da dee la da da)",
            true,
            10228,
            72898
        ),
        new("death grips", "takyon (death yon)", true, 17626, null),
        new("flume", "what you need", false, 1255, null),
        new("fox stevenson", "all eyes on me", false, 906, null),
        new("fox stevenson", "bring a coat", false, 201, null),
        new("fox stevenson", "don't care crown", false, 749, null),
        new("fox stevenson", "give me some space", false, 314, null),
        new("fox stevenson", "glue gun", false, 421, null),
        new("fox stevenson", "human in the evening", false, 405, null),
        new("fox stevenson", "road to nothing", false, 301, null),
        new("fox stevenson", "that choice", false, 471, null),
        new("gyari (ココアシガレットp)", "アカリがやってきたぞっ", false, null, null),
        new("gyari (ココアシガレットp)", "チャンネル登録よろしくお願いします", false, null, null),
        new("kreayshawn", "bumpin bumpin", true, 234, null),
        new("kreayshawn", "go hard (la.la.la)", true, 7099, 2034),
        new("kreayshawn", "gucci gucci", true, 31186, 20461),
        new("mitsukiyo", "unwelcome school", false, null, null),
        new("nimino", "orla", false, 324, null),
        new("nimino, manta", "better", false, 734, null),
        new("nokbient, tiny waves", "opinions", false, 5, null),
        new("nokbient, tiny waves", "pfeilspitze", false, 5, null),
        new("nsync", "bye bye bye", true, 50956, 140199),
        new("run the jewels", "legend has it", true, 4259, 3739),
        new("run the jewels", "ooh la la (feat. greg nice & dj premier)", true, 3481, null),
        new("아야츠노 유니 (ayatsuno yuni)", "supadopa", false, null, null),
        new("電音部", "eat sleep dance (feat. moe shop)", false, 500, null),
        new("Creepy Nuts", "Bling-Bang-Bang-Born", true, 4530, 39689),
        new("Disclosure", "Latch", true, 6368, 49928),
        new("Disclosure", "NO CAP", true, 559, null),
        new("Adele", "Hello", true, 8248, 289222),
        new("Adele", "Rolling in the Deep", true, 234367, 119530),
        new("Adele", "Someone Like You", true, 183030, 82251),
        new("aespa", "Spicy", false, 1660, 6325),
        new("Busy P", "Genie", false, 132, null),
        new("Disclosure", "Waterfall", false, 1027, null),
    ];
}
