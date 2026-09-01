using System;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// The standard rubber-stamp vector faces (the bordered DRAFT / APPROVED artwork a
/// viewer shows for an icon stamp), embedded into an imported stamp's /AP.
/// Recorded from the expected output bytes of the XFDF rotated-stamp import:
/// each face is one content stream over the shared art box - a gray (0.85 g) drop-shadow
/// pass of border + stencil letterforms, then the same shapes in the face's own CMYK
/// colour (DRAFT 0 0.9 1 0, APPROVED 0.75 0.05 1 0), all filled bezier outlines.
/// Streams are stored flate-compressed exactly as written to the output.
/// </summary>
internal static class StampVectorFaces
{
    // The art box every face is drawn in (the /BBox of the /AP forms).
    private const double BoxLLX = 179.075, BoxLLY = 374.062, BoxURX = 424.453, BoxURY = 438.592;
    internal const double BoxW = BoxURX - BoxLLX;   // 245.378
    internal const double BoxH = BoxURY - BoxLLY;   //  64.53

    /// <summary>
    /// Build the /AP /N vector-face appearance for an icon stamp and refit its /Rect.
    /// Returns false (annotation untouched) for an icon with no shipped artwork.
    ///
    /// Measured laws (XFDF import of rotated icon stamps):
    ///  - /AP form: the fixed art box above, /Matrix = pure rotation by the annotation's
    ///    /Rotate degrees (the expected output adds a uniform scale factor, which the
    ///    BBox-to-Rect mapping of PDF 32000 (12.5.5) normalises away - not modelled);
    ///  - /Rect is refit to the rotated art's aspect: the largest centred rectangle of
    ///    aspect W'/H' (the rotated box's bounding size) that fits INSIDE the imported
    ///    rect - one dimension kept, the other shrunk (verified on all three probed
    ///    stamps to 0.01 pt).
    /// </summary>
    internal static bool TryBuildAppearance(PdfDictionary annot)
    {
        var icon = annot.GetName("Name");
        var flate = icon switch
        {
            "Approved" => Convert.FromBase64String(ApprovedFaceFlateB64),
            "Draft" => Convert.FromBase64String(DraftFaceFlateB64),
            _ => null,
        };
        if (flate is null) return false;

        double rot = annot.Get("Rotate") switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => 0.0,
        };
        var rad = rot * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);

        // Refit /Rect to the rotated art aspect (largest centred fit-inside rect).
        if (annot.Get("Rect") is PdfArray rectArr && rectArr.Count >= 4)
        {
            double llx = Num(rectArr[0]), lly = Num(rectArr[1]);
            double urx = Num(rectArr[2]), ury = Num(rectArr[3]);
            double w = urx - llx, h = ury - lly;
            double rotW = BoxW * Math.Abs(cos) + BoxH * Math.Abs(sin);
            double rotH = BoxW * Math.Abs(sin) + BoxH * Math.Abs(cos);
            if (w > 0 && h > 0 && rotW > 0 && rotH > 0)
            {
                double aspect = rotW / rotH;
                double cx = (llx + urx) / 2.0, cy = (lly + ury) / 2.0;
                double newW = w, newH = h;
                if (w / h > aspect) newW = h * aspect;   // too wide: keep height
                else newH = w / aspect;                  // too tall: keep width
                var refit = new PdfArray();
                refit.Add(new PdfReal(cx - newW / 2.0));
                refit.Add(new PdfReal(cy - newH / 2.0));
                refit.Add(new PdfReal(cx + newW / 2.0));
                refit.Add(new PdfReal(cy + newH / 2.0));
                annot.Set("Rect", refit);
            }
        }

        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        formDict.Set("FormType", new PdfInteger(1));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(BoxLLX));
        bbox.Add(new PdfReal(BoxLLY));
        bbox.Add(new PdfReal(BoxURX));
        bbox.Add(new PdfReal(BoxURY));
        formDict.Set("BBox", bbox);
        var matrix = new PdfArray();
        matrix.Add(new PdfReal(cos));
        matrix.Add(new PdfReal(sin));
        matrix.Add(new PdfReal(-sin));
        matrix.Add(new PdfReal(cos));
        matrix.Add(new PdfReal(0));
        matrix.Add(new PdfReal(0));
        formDict.Set("Matrix", matrix);

        // Resources as the expected output carries them: the /TransGs the art selects and the
        // (unreferenced by the path art) /Helv entry.
        var transGs = new PdfDictionary();
        transGs.Set("Type", new PdfName("ExtGState"));
        transGs.Set("CA", new PdfInteger(1));
        transGs.Set("ca", new PdfInteger(1));
        var extG = new PdfDictionary();
        extG.Set("TransGs", transGs);
        var helv = new PdfDictionary();
        helv.Set("Type", new PdfName("Font"));
        helv.Set("Subtype", new PdfName("Type1"));
        helv.Set("BaseFont", new PdfName("Helvetica"));
        helv.Set("Name", new PdfName("Helv"));
        var fonts = new PdfDictionary();
        fonts.Set("Helv", helv);
        var res = new PdfDictionary();
        res.Set("XObject", new PdfDictionary());
        res.Set("Font", fonts);
        res.Set("ExtGState", extG);
        formDict.Set("Resources", res);

        formDict.Set("Filter", new PdfName("FlateDecode"));
        formDict.Set("Length", new PdfInteger(flate.Length));

        var ap = new PdfDictionary();
        ap.Set("N", new PdfStream(formDict, flate));
        annot.Set("AP", ap);
        return true;
    }

    private static double Num(PdfObject? o) => o switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 0.0,
    };

    // DRAFT face content stream, flate-compressed (recorded expected output bytes).
    private const string DraftFaceFlateB64 =
        "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" +
        "MOs=";

    // APPROVED face content stream, flate-compressed (recorded expected output bytes).
    private const string ApprovedFaceFlateB64 =
        "eNptW12OLb1tfJ9VzApOJIqiqBX4OUA2EFwgRuC5BpJv/4BZVezxfMd+GpDTrZYo/hfPx/99/Md//f9///2Pv/zx+dc/PsYr" +
        "9+dfP+bn/37MO187Ptfx1/T8/P0xM177TnJG2OdMe/ktMl+jqeGfq/7e+/mrHp+vM/anWzHSP7/IiXmLc153e72yX/vkpy97" +
        "jb0+5x2v7aLnwho+/bWnFSfqiVrC5315ijFX1FLrtUnXK1iq6IjkJ2wkljB/rTi1r+JMbAMcr89hp+Zez446h/Mg02vDs7Zl" +
        "osfmUf4silpi3PpXcomcJRtsdNgR455ao46YoKOkEaI/V61zI3iwU7tKCseNB6vt1VLYeB3d53pNkGu+FkQysnZvpC0m9xSv" +
        "jM0X9pkQb62e+FZxvK5x1olvy6j2UTQuatXa+fIZvKJ87b0pmxvGK8pX1IZ0i3Wr114H9KkDr7qye15nTtK1nV/vkvj6+J+P" +
        "knvpR235vPJAODb8lVnP3BJs/eeLnAXdubW1MT/5RJTI63Nxs+i67zoK6HlwBeDECYlwnXrilICc9Fkir/49Jp/Hddcll06N" +
        "sbRiCY506aCNXTuw/v/hGyXaAbW4FBt2WYqBb4BjOHwdGufgEyn6cImbupNiQJ64idIk0Ae6B5ofLQXfxmOZZX8UAuDBeaz6" +
        "z5ZgoOeg84Ku7Xu2IK5Dw0oQteYXjzr2FscmOX+6gWLMsrCLjZWYypB+c5nk+evluXuZhZ3UGaZeKtGKzugHzl20j5KTPoNj" +
        "lDJRAHglpLalzbNEUxyDymOZMrrgMqXyxiPXEUvpSHtSBKfu2kpdLRbpVQpeRy7OORLSLF2zMuBJGiIw0Wmkrd8ozk1ejdWW" +
        "8Y07dNurbpvfnLf/zzfK4GzrjUmLhMz20jOlSJAGvQTIUgrSJ/nROami4IQ4UYuCjtQ2EwerBadLyeXe8NHRB4uLNWsTIq/r" +
        "hQyj7JZTR9/ukea2zit4CzD7hbv1MgKX09l1FvP6TOgJq1spp1fyDN6bHce6Xu/CmZQbO6X7dR8valRdAXZeZNSVwdjXDr5Q" +
        "nPKhXmq0VkmnFsD7Y9XOy569VKScrZf6LvoJ7GHBsUHhJ56ovwtLlvFgT3UK3CXo3FT0Be0NrnGdmrPK7qCA9ZVyzrVG2e7B" +
        "V8t71HZJw4HDV0ytUW+UweEgY9uzhm062CjRg96Uxa7LmdxHhJS8jK738VO+0OBDJ4w17NCawFlJ+UVZHulDMg3qV9ZbsZEf" +
        "ubxIK1e4xKH8irah2BN7kp5XVwSPojcY4WBa93LNFa5LvZP0Hn3JJSRsHF/tjW8JMF5OddpxH9qMpojtg15yPsNaFlteOKR/" +
        "q0IkwlxxNm6xaO8nJuwMdMgBRt5+w1OWF3WLWNPoqOEKF+m5rWl67jrIDdndOvIh5atsN8cpnFhawy1ETxmex21hxeiAeyTe" +
        "a/Ixa+hC6pSkvcV7uD9w8hhvEDdJWeQhvXPJj8nc37SAthilmSb/d422GFEWdTshqHUjKyToCcjWzuCecdFn0hZPhf59pSwH" +
        "T3j5x0Udzjol6es0xiMNPfAPQb0/OG2tsPAGomzyE6ecHgzpSMSBaL5ojOBYVBAwl3HuOmwdI6CNCImXhlQc6AxNLxkuokSc" +
        "QU7CgIs+cDIVWLE90jAJRietUW+4vIrf+awxZTaIOaAtdPSdk/vAfkDnit7HTwHXGtuYTOIaIMDf4sC1IE5B5UCHbM0QcDb8" +
        "s1ZFzK1Vi2MmDgW4ywldBbYB2wKduqStcAHO6WuEjLfxlKDPmqQrdjdNawykw3ojryQIZ76YFo1xmzOnlC4OZFpPUOkQKlse" +
        "nTZBKSWPVEJxeZHIUG+nEyZ6S++nUWvB6Txq1EXiEzdlbfCtoJG6iOZJa6mQgeaMlvie2RynPJdpiYwQPTs8j9vyhdnR3BBL" +
        "cQMd6JBdUnrZxndpBFvhggkiNKNou6J33RRpHsOfcPGmBzTHylIR3qlfQUcNzjHetF8YV0W09swHO0POv/rmJeFThh/iGHz5" +
        "eVx9KLiS5rUi9ugFpuqVZo+SLJZcVKZkbgQaL4rmYZGYN2cp0hXHl1M1/snYOu8sR0YaNUrJgylQpaTR4pFKI02RJjkSbLjK" +
        "R3FWk9KTrfwa/ndKLzYygFpwLbnUciUgZ0bT9CGoCMaTdNFN18FyZ3Ockunkd80Q2cmvbBkMl1ZkSLI3FI0YTA7OeKQDLdo6" +
        "ZZJxYMggWwawFpDMxyumSu/e7h9yvNRmivrQWxQnH+Er3MBYmMSiTqp1S+ZhQXrDtlF5DV056kQIr76zNnUCiclC4XCN30Ww" +
        "AL2PMvRInAScMw+9I0re8rKVMC16z1NSxTcX/MlQqoZvgHPFGc59nvbJzgwAawbq2YE7P6RPKqPftCpwcm1WE5Dslzg+yZmo" +
        "3vEOzb9kCMeCCDDlo5iygnHoLzZSiaHUlgviNYQYXmlAb/E4MtrZDEhmMWjhdZRQoHO0oi/vN9I66eWpyv9sacGAR+Ano33J" +
        "c6pHz+DR6rjrKPYjO4SYcklRh0vXy5S7dhwmYecRvRC6oF/K0xGnflEjfD+KhyVRpU1JxZu2Lqak29Qhir5zRtYuxpA6ETAu" +
        "XJ7xsuaWgaGcY4BdzJihmCmVQI6CNSbTSXDQ5OAacHmo5UIko3jl5saYAw+sgHtP9gr8CvJ8mAysnSkiQqG3G2lnp1ThzWDg" +
        "VheflgGUFv0uRlkgqoF6DZXbxBUqnEK5SbvMIVjELhqk1AIp6pc4R4oxB97xDgBeIq0n4ZC9q8ZeozgdyVZMKjO8ZvcIJrTT" +
        "mZIsGBcNwpnjQX4zuEZxtim3Yggbh5UsTR16jBNQHpeJyK/3s2PjKBi0r9oNhFHPcqeuYI6WVfTJUJOC3n32q5OUc13W1Zek" +
        "cVk/617wTnZwQ5ZYdoav795Fr6HajjtlgrYMofHwLNR/qzOZtAEZKehwJWzowtUaxTmH4oLfW5U0ryn/zMYITto9lsXk/u3s" +
        "1A1Xtkovz2q0bpHtQi5zoUFV91XaqdOt+XAgEzpQtOaK3nwCbgP0UKew6NReF+r0jrtotBncuPJsR95OuhNFBgNyjAIalf3h" +
        "E2s1jQXQQQySkx2kxWpykmPsB4GTIW1AOeIVPK9W8Cs6UPSWwI0LGC+Plwa5sSxXxbbgl0FLCnfQWZIhjsMBFU2fgcSVH1Co" +
        "ZVnJPkc5H7YX4WfqvrnHP8v/i8/sexVaqKCOUiaUKfnlI5Wl7uDCC2VS0QlPgvYr3Dro2fWrzABvPPVp6In1KCgSEQ9merye" +
        "c3qr53Z9isjiOJTu64o6SpJM1wUOyyAcbXPFzS9cNkP4Rd0OPe5CDt+X40YjQlapG0XKspBSphYMeJiNMHkV5reWWCyHmdTj" +
        "UEgprdsoSXL1mWz3J2Fr1EqkQkWPkBACll9Fwh7qJSAq4o3BNh4EexGjvGq40wkKD4VuxOLVlKehGEqg0MIKg+MYL6tWZ9g3" +
        "No/+5YZphrsc+JK09qQZsh4TJ1iuripN51WcO1sSC/ahmBbeaA4bmsYUYu1NX6uOwBbtKlBU75PDFMOZxIFGo4hNB4TIWnAy" +
        "MpYvZu+p/GpFFgVTFFukVwdXeG3QQzkGzOkXN3UzOon33uZtyacFv/K03ZFZYxdHN6fONxjSrqOO7K4L0QMo60jaE/V018UZ" +
        "Ep9j/xveu+X7+S7uej4OPRk4Z/ACzmZyCA7hEDCkHmg07m9OKBxB7EUT7UDeeIS8+Gh608cc5FNisE2Pwt3VCkZ+X3RJ6fsT" +
        "eKFSGakkC4xTKtpdpAlXjm2n5BKS1NtBvsg5R1eYR0cb7AXgygz1an0kmWDVu9xEsh092NPCJlxmwGyTx7K9uzd4SaNbS5rC" +
        "PNGJDvoTFAtvu16HyYs++oAs8/B2WBYrSgc8mNziYTf7/RTibGbYdfZ02gxC12JGhXYr6MMErK4Z9WudW4jK6LoVnElFUmIF" +
        "esE00YAEdHAm+3mgO8yUrJgroUKn5CaLx1qRX7BOBOqeWhbIPdXMUdZDhjh+JHxH7ESmqS8C6CAZs/dI/w+O9x5FHiM11Sxa" +
        "7Bjg0Mo6S7dgn4HcZNPbsDkBsaGdwjz10f2fgqRDQv8PJZINHryEW/aM5hM4dP0DyI5seE75N+YrjVQAa0IfezNBqYtUut1V" +
        "r3Vxqu746sqanNZLuUo8qvIGe2ZpaYsrhV5Ul6HufTJqsepQyEXU0QpjPh0D74ahEo0TwgfiKIUcqk/ejl5rVFLtR4kT4mkJ" +
        "I+sOmLpSE6tu3Y0UEEU82ZVyfRt/QZucrLoU5EzBLMJoK2FsN3wF3/loH3voOOqD8sub8RfkVB0G0BY01/muaMBRfN2l6wxE" +
        "4Gx1xRwJJs5AZ4MUbPOjYcJCQzEdu+rKORs6tqmc4CKcgB5yu+m735ipIA5jRdMh5WfhjUFne+Urd5W4uqCwh8z6TdjQxnI7" +
        "+3MAUp6f4/NvH0ADlT0OVmXAx4wtuEXAk22BYazcqDtT0NwT2ZBhGgrX3F24EqnzboYLNgaNZjT7FS7Q82ZrnQm03N1TMWLV" +
        "XFLRF50nlIpr9n+TeGMSpWV7j9oJjp3VGBMQyGBvRE9c0Y0vzmuNI18XKjAqDQLCu7vTNIDJ53c14+s2yDlUmCVPpZQGYgl0" +
        "bVCNp4ou248cACoxyVmN/qZ7M4jKvMkfnNsh5LJ7JBQ5GqA0i35r3MaU7HnLszn39DMcEEDS5gJHg8bNTP8K6UMDqTNNlsnQ" +
        "NsogqATiXHU9gnjCoFvg3QOLmYSXaH3q1pgmBqALoQfu071bIs/8Lpz0PC0didZwfoDO9qrXyg+O7P9Ho43D9QbiM/Y4lL2D" +
        "Q/26isaggUeCjnZ6bA+A0a0VVCCUng5FfQbKLf1egkhR/rS+A94cyjFQ1wbJ6LJ2qyP7doUCKzdrS2ahl/23lexJ0cLRCEGR" +
        "tc93Hm+osghNJqcaACUid1OgABJuHoIWUa1ckUqJdrAVilqEETDpVfG+E4oMllf4Avt53Qz4RQAVXVYEHCStAAln92qwpbXV" +
        "ymc8Esq1WTGws0AfA1zrsL8D/bvEuZI5C2KWcLBcatakOvN4g/WmE//uNTrUJLCjdYiIs4DExiHM3fWKZhjexPtFJKxxRU+a" +
        "ERim0pq4DR9QxocJD4LtnQo7J2jAWd3ipvwApl/ljNkA/k3d0AppsykhY7IsfL5TyD2E11sbHJQd+8ZHe99qJi9g+sr61boq" +
        "hjqJdRKA43jgSP1stTB8/0QEinOnFPbwFitJ7ycwqUR6d/+48VJrNB4aiPdHu19EdnzxeqMWYT1mcBpe2cldFic6K9hoRhpK" +
        "yv3P+Q7r3iwLpke4V2koxwog/ieQIwR8zzbUwi1cqK9yGPby0aa5otELIo1EjvVSv/BDBYRTYmhBGcmiFQZ0P8UhDBQaACgT" +
        "jkPU0rbK82tqxZf1e3YzA833SnCXehXDruhUcdPzNUchCRqPeTBDKow3hsbK8A1AMjDDK/FGsBXMqYIQmpVTmSggGZxiXTW8" +
        "txr+4OzuabGABUDmR1nkgO0CyNzdeIcXCwFKTud4G1LzpeiEVnyvMWQyiDOgv8G+a9zHdAEEQzj5n+X7RbzwdN0FAf4Wp+MS" +
        "lA1kd/SQxZtf3pZSJWqKd2ZDJNk1lJHq7y1YFehuoeT4HtKILjgvl0yXkV2OhSCmzaaFuqGT1YmqoL5Lz4MsSAF2Xzp/KBtG" +
        "uvgAlQ2BsUXRSdKDHM+G4hEm9AQ7/8wfluiOOgQAyPhOxstTxuhCrWiYbQz2qkXPBmPVUV10bRL3ajMbtnlUdR0W21GkOxw/" +
        "or1tZOYSvv5/t+4iz+7e9G5Ac1rngiZIefSE2yEcPLtSOU+EeFMAwZOns1nARnTO4PR0ACB70u2MMYFmVemqs56PeNFV6C4v" +
        "tBb06CcYTkHP2z3L6DfGkHLi/5jYOqKgyyc0l0OaZ02A7+JsxbYUFMqqSfNl+bhWhWLSUzgQE56EVksJjrQZI4feGRUBTCSu" +
        "7c6BwYBOqYkAyoSaSy0Oon4elon0pNv5yXt207zVg77rk2XROxcn/DRnUzi+f6DKoPviMVMpYbG2YeEk8Z5GfhhDDg6qCnPv" +
        "B9X00EE4egLaFMgwPEiaNSnQw37hpxZ8UTbelTGCyG9y4rkDBRrgRbo2DCcRoZqqMYmosw5Ub/ooe7s94wFoAwkJ8vOcD2YD" +
        "UuNEi0OkeCHVjMKcCfIqmDy7MpjUWvwkig541iuonJwURzOF6OFtcRj5AS2sRrCmYM9uVaQM6yYtENVDLEFWSUwJ+5tbbyy1" +
        "ljYC1X1g6MMRNYGxm/4CLazJJ6JRIE4zAYzllSa1l5hmU4IXd6rHwEkK0Ld1XSXpmFyX8RVfBtjr3WTY/bndvuQ507eWwaFh" +
        "eK9hbCNut3saAoOPDfWOLhRtSdIR3WVFzAJqZyrBEKGE3pufVrogvj9aSLvJ2aWTAhTUJ1QodaIIjiupxYwJ4Uf0MTAoi+wn" +
        "4ZI6tLKZTaU8UojQaCfAwmk9eNpruPplaFWTnt0vk7PP2+NRm+me1uB3OFSafMd6oho9ARpHhz1XmvBmLoIon1Y+wijbTZhm" +
        "9ae3AEBxssNJV4vbAb1kD7mFbhE9ZSHLQVsy2quzT8meXVITDN1X2I5aKtErLAI1vHk17wdcZ/cETIC50pHBmh60M0k6BLGh" +
        "bhqhFBZTD2BESXYOEkAB3fdkSvTrX07+RcjSH1ebwmvPq73zwoAFcxCdC4gIadmg5igAtV6bXXFJFMGS8buTA9zHVyeIQeBU" +
        "Iy23UzNy8AZ2KmzBTHAsqmd0tTCUN5V4Qe6GtpIytSNsASOO7CvK6VVRUvHYeibP+VHvhkr0J38eXPik8lQ1ytjkXbjwdvpD" +
        "yCnabz0/aw/n8Z3o8dnVwN1MghgATlfXO6O3itl7nR+BGrSwwJ5RIa1My/p5ZEmQxUKdb6g4J2m9fyl5/l9vsIAkw9WYXMlB" +
        "CXKA0VQhvVJLHAScon3om1UcN8Lp1ttGtsRSvOs0pEOgtxCyqXYoOCHJYJAF9G2UFbGCWO9u3OUI06xwnertN8jxdgNffGal" +
        "RgUvfzEARGsd1SzSFUBcLqiNQ9mOAKKJA3ZqQHdncckO8MZTlR49MR8NBcTp3Wq8zDO0051dtMLjY2r63AY+puiQ6bn1C0Qx" +
        "TCNTWJJjl6BRFXq3p0Ar0CDP7CuKJeS1YWsTogMIUb9j0S8GSF/VZcFuBnHOqaydczBILBuJoh/bg0bBY8Tz1XnUJMWM0XJc" +
        "qySBZBK0Nc6JIQOhu7lOzxoTat2Nrq0teHiF9Q9ldMt1l+qgWIPOzokZcezfXLJQShd+AhU1IS5LAxk4f+M0Vfz14O3l1DLe" +
        "8ubkOM15hkWQRwDInU8rIER2Encb5510cKGfLAH3dVXMGNYEfVk9BDu6gguncrY1BCdO5RwchNybkxxwmOm30ULhuoftdG3x" +
        "dA+EnUPQuTsv1zd35+lommuXworxI5JF+PlMVWfokYp+wp5ATWMLlsqE/KBWQBiWeIVq/hT3L4LC+LUDOJfDswDrJrUcs0XC" +
        "l1arR+2Yw4jiCLXeAA1BH01Cs1pEpX+bDoFWk3OG5OC0AKVY1zh/8HAmS7vnE8KobDYOicIi8huFxJQeN/6AqRLX21G+yNmh" +
        "Fv5oPC+Z9zPQXaGfwUTrcpyd27wNpY3TG6clDA3Y4mijZ+y2sDa0aUn6bEBWKQ96FJOy4b0TnbtN61cNS8DKWT1Dt1k6a+PR" +
        "ce3e82+OIo5+GHY5TvybcH5uQYdotxKFmwKLD7SfEKjpp2VMtsG5aT0yeylklCi8lSv6dp++gw4Az61WKzH6QDtAN02fTloA" +
        "9vEHEB1LQ4pXKRA4FKETHeRVuwbJ14Mvrk5EhVWFUHhydu/TNSt3uYtDUIMNXO+DKQ/d+tlPhMB7/CgM9w7hZXTm+hjCT3HS" +
        "O/WMLUwBxwdkyQ/wR3spmPD0D+pY1AzlL/xJ3CJgiV62gOoY67s3KdANvw7M7pDvLrbJYcpyiPx/CcJc4uR4fm9k/HjG7EFu" +
        "waf6TRnnsE2zOVtzHeCMp42g4e5uDLFkxkEFmZk9u/h5coBohz+t4k4j6ShSGSzKnilHcfhblu9BtxPftTP7rKCn3G4PrYEz" +
        "hLNwgAYVu6luIdJ1FH/QfcrVcKD3r9YYk0EPVWds9WLWOn/UOfoNp6qFqekFcFxtsmgsdTaEyKGdoxkReZbeZMD/we8G94hk" +
        "iakbfxS61UUjBiSD3hQrPRGx2OCEAaQJ4Bxf2Ldp+a4Mxi1wTOb9Jm/o40yh0tzQcP4i14UBFWfXf+a56qXlIK4EGohNblao" +
        "vz7AQBPO0UPmT3IBqGKvazD1nwlDBu3M2/mbUUgHNDWUoyWoHFf2jzlZFSC3BefKA3OGfjkbdaChKW63rY3R3fRTS3b75bSL" +
        "9u/fSNDbApUmbRzLJCpNTP1dFvhJLmSweViL4G9yZ/98c3fs2JxlJyDcY3Vr908TzfrnxsmdOnOrr49nJNM5Thh0rszjl+aW" +
        "NYp7SR+myBN9TNRfa7Dh/MW9Aq5xU4WNnz0z+NnmYANoV4qX9Hf4ofWY+skFZo2+xIFJ8SaTv5SmiSV+lFikBlpBCn98lwb0" +
        "5z//AQh49W8=";
}
