// Pruebas del núcleo de PrimerIFVG (sin NinjaTrader). Se corren con ./correr.sh
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NinjaTrader.NinjaScript.Indicators.PrimerIFVGNucleo;

static class Pruebas
{
    static int fallas, total;
    static readonly DateTime Dia = new DateTime(2026, 10, 27);   // martes

    static void Afirmar(bool ok, string que)
    {
        total++;
        if (!ok) { fallas++; Console.WriteLine("  FALLA: " + que); }
    }

    static void Igual(double esperado, double real, double tol, string que)
    {
        Afirmar(Math.Abs(esperado - real) <= tol, que + string.Format(CultureInfo.InvariantCulture, " (esperado {0:R}, real {1:R})", esperado, real));
    }

    static DateTime H(string hhmm, int dias = 0)
    {
        return Dia.AddDays(dias).AddHours(int.Parse(hhmm.Substring(0, 2))).AddMinutes(int.Parse(hhmm.Substring(3, 2)));
    }

    // una vela de 1 minuto que abre a la hora indicada (hora NY)
    static void M(Motor m, DateTime t, double o, double h, double l, double c) { m.ProcesarMinuto(t, t.AddMinutes(1), o, h, l, c); }
    static void M(Motor m, string hhmm, double o, double h, double l, double c) { M(m, H(hhmm), o, h, l, c); }

    static void Correr(string nombre, Action prueba)
    {
        Console.WriteLine(nombre);
        try { prueba(); }
        catch (Exception ex) { total++; fallas++; Console.WriteLine("  EXCEPCIÓN: " + ex); }
    }

    static int Main()
    {
        Correr("Agregador: velas de 5m alineadas al reloj", AgregadorCinco);
        Correr("Agregador: minutos faltantes cierran la vela al empezar el bloque siguiente", AgregadorHueco);
        Correr("Agregador: 4h desde las 18:00", AgregadorCuatroHoras);
        Correr("FVG alcista de 1m: se invierte (IFVG bajista) y muere al cerrar sobre el CE", IfvgCicloCompleto);
        Correr("FVG que caduca sin invertirse (límite en velas y en minutos)", IfvgCaduca);
        Correr("CISD bajista: open de la primera vela de la serie que creó el máximo", CisdBajista);
        Correr("Vista previa de IFVG y CISD con el precio en vivo", VistaPrevia);
        Correr("FVG de 15m: nace, se selecciona por cercanía y muere al cerrar a través", FvgQuinceMinutos);
        Correr("Liquidez: PDH/PDL al empezar el día, sesión de Asia y barrido", LiquidezDiaYSesion);
        Correr("Liquidez: swings iguales se vuelven EQH; pegados se quedan con el más extremo", LiquidezSwings);
        Correr("Deduplicación entre temporalidades", Deduplicacion);
        Correr("Sesiones HHMM-HHMM", Sesiones);
        Correr("Tres días de precio aleatorio con huecos: sin errores y listas acotadas", Estres);
        Correr("GEX igual que la calculadora en Python (cadena con gamma)", () => GexCadena("cadena_qqq_sintetica.json"));
        Correr("GEX igual que la calculadora en Python (sin gamma, IV en %)", () => GexCadena("cadena_qqq_sin_gamma.json"));
        Correr("GEX: strikes extra iguales a Python", GexExtra);
        Correr("GEX: JSON sin precio del subyacente", GexSinPrecio);

        Console.WriteLine();
        Console.WriteLine(fallas == 0 ? "OK: " + total + " comprobaciones" : "FALLARON " + fallas + " de " + total + " comprobaciones");
        return fallas == 0 ? 0 : 1;
    }

    // ── agregación ──────────────────────────────────────────────────────────

    static void AgregadorCinco()
    {
        var a = new Agregador(5, 0);
        var cerradas = new List<Vela>();
        for (int i = 0; i < 10; i++)
        {
            DateTime t = H("09:30").AddMinutes(i);
            cerradas.AddRange(a.Alimentar(100 + i, 101 + i, 99 + i, 100.5 + i, t, t.AddMinutes(1)));
        }
        Afirmar(cerradas.Count == 2, "dos velas de 5m");
        Vela v = cerradas[0];
        Afirmar(v.T == H("09:30") && v.Tc == H("09:35"), "la primera va de 09:30 a 09:35");
        Afirmar(v.O == 100 && v.H == 105 && v.L == 99 && v.C == 104.5, "OHLC de la primera");
        Afirmar(cerradas[1].T == H("09:35") && cerradas[1].Seq == 2, "la segunda empieza 09:35 y es la número 2");
    }

    static void AgregadorHueco()
    {
        var a = new Agregador(5, 0);
        var cerradas = new List<Vela>();
        foreach (string hm in new[] { "09:30", "09:31", "09:32", "09:36" })
            cerradas.AddRange(a.Alimentar(100, 101, 99, 100, H(hm), H(hm).AddMinutes(1)));
        Afirmar(cerradas.Count == 1, "la vela 09:30 se cierra al llegar 09:36");
        Afirmar(cerradas.Count == 1 && cerradas[0].Tc == H("09:33"), "cierra con la hora del último minuto que tuvo");
        cerradas.Clear();
        foreach (string hm in new[] { "09:37", "09:38", "09:39" })
            cerradas.AddRange(a.Alimentar(100, 101, 99, 100, H(hm), H(hm).AddMinutes(1)));
        Afirmar(cerradas.Count == 1 && cerradas[0].T == H("09:36") && cerradas[0].Tc == H("09:40"), "la siguiente empieza en 09:36 y cierra 09:40");
    }

    static void AgregadorCuatroHoras()
    {
        var a = new Agregador(240, 18 * 60);
        var cerradas = new List<Vela>();
        foreach (string hm in new[] { "18:00", "21:58", "21:59", "22:00" })
            cerradas.AddRange(a.Alimentar(100, 101, 99, 100, H(hm), H(hm).AddMinutes(1)));
        Afirmar(cerradas.Count == 1 && cerradas[0].T == H("18:00") && cerradas[0].Tc == H("22:00"), "18:00–22:00");
        cerradas.Clear();
        foreach (string hm in new[] { "13:59" })
            cerradas.AddRange(a.Alimentar(100, 101, 99, 100, H(hm, 1), H(hm, 1).AddMinutes(1)));
        Afirmar(cerradas.Count == 2 && cerradas[1].Tc == H("14:00", 1), "13:59 cierra el bloque 10:00–14:00");
    }

    // ── IFVG ────────────────────────────────────────────────────────────────

    static Motor MotorConFvgAlcista(ConfigMotor cfg)
    {
        var m = new Motor(cfg ?? new ConfigMotor());
        M(m, "09:30", 100, 101, 99, 100.5);
        M(m, "09:31", 100.5, 105, 100.5, 104.5);
        M(m, "09:32", 104.5, 106, 103, 105.5);           // vela 1 (09:30) H 101 < vela 3 L 103 → FVG 101–103
        return m;
    }

    static Fvg Fvg1m(Motor m) { return m.Ltf[1].Fvgs.FirstOrDefault(f => f.Bot == 101 && f.Top == 103); }

    static void IfvgCicloCompleto()
    {
        Motor m = MotorConFvgAlcista(null);
        Fvg f = Fvg1m(m);
        Afirmar(f != null && f.Dir == 1 && f.Ce == 102 && f.Estado == 0, "FVG alcista 101–103 con CE 102");
        Afirmar(f != null && f.TBirth == H("09:30") && f.TcBirth == H("09:33"), "nace en la vela 1 y se confirma al cierre de la 3");
        m.DescartarEventos();
        M(m, "09:33", 105.5, 106, 102.5, 103);
        Afirmar(f.Estado == 0 && m.NuevosIfvg.Count(x => x.Tf == 1) == 0, "cierre en 103 no lo invierte");
        M(m, "09:34", 103, 103.5, 100, 100.5);
        Afirmar(f.Estado == 1 && f.TInv == H("09:34") && f.TcInv == H("09:35"), "cierre bajo 101 lo invierte");
        Afirmar(m.NuevosIfvg.Contains(f), "avisa el IFVG nuevo");
        Afirmar(m.IfvgCandidatos(H("09:35"), new[] { false, true, false, false, false, false }, false, 0).Contains(f), "se dibuja");
        Afirmar(!m.IfvgCandidatos(H("09:50"), new[] { false, true, false, false, false, false }, false, 10).Contains(f), "se oculta pasados 10 minutos si se pide");
        M(m, "09:35", 100.5, 102.5, 100, 102.4);
        Afirmar(f.Estado == 2 && f.TMuerte == H("09:36"), "cierre sobre el CE lo mata");
        Afirmar(!m.IfvgCandidatos(H("09:36"), new[] { false, true, false, false, false, false }, false, 0).Contains(f), "muerto no se dibuja");
        Afirmar(m.IfvgCandidatos(H("09:36"), new[] { false, true, false, false, false, false }, true, 0).Contains(f), "muerto se dibuja si se piden");
    }

    static void IfvgCaduca()
    {
        Motor m = MotorConFvgAlcista(new ConfigMotor { MaxVelasInv = 2 });
        M(m, "09:33", 105.5, 106, 102.5, 104);
        M(m, "09:34", 104, 105, 102, 104);
        Fvg f = Fvg1m(m);
        Afirmar(f.Estado == 0, "dentro del plazo de 2 velas sigue vivo");
        m.DescartarEventos();
        M(m, "09:35", 104, 104, 99, 100);
        Afirmar(f.Estado == 3 && !m.NuevosIfvg.Contains(f), "a la tercera vela caduca y ya no se invierte");

        m = MotorConFvgAlcista(new ConfigMotor { LimiteEnMinutos = true, MaxMinutosInv = 2 });
        M(m, "09:33", 105.5, 106, 102.5, 104);
        M(m, "09:34", 104, 105, 102, 104);
        f = Fvg1m(m);
        Afirmar(f.Estado == 0, "dentro de 2 minutos sigue vivo");
        M(m, "09:35", 104, 104, 99, 100);
        Afirmar(f.Estado == 3, "a los 3 minutos caduca");
    }

    // ── CISD ────────────────────────────────────────────────────────────────

    static Motor MotorConMaximo()
    {
        var m = new Motor(new ConfigMotor());
        M(m, "09:30", 100, 100.5, 99.5, 100);
        M(m, "09:31", 100, 101.5, 99.8, 101);            // primera vela de la serie alcista: open 100
        M(m, "09:32", 101, 102.5, 100.8, 102);
        M(m, "09:33", 102, 103.5, 101.8, 103);           // máximo
        M(m, "09:34", 103, 103.2, 102, 102.5);
        M(m, "09:35", 102.5, 103, 101, 101.2);           // con 2 velas a cada lado se confirma el pivote
        return m;
    }

    static void CisdBajista()
    {
        Motor m = MotorConMaximo();
        CisdNivel cl = m.Ltf[1].CBear;
        Afirmar(cl.Armado && cl.Lvl == 100 && cl.TStart == H("09:31"), "nivel armado en 100 desde 09:31");
        Afirmar(!m.NuevosCisd.Any(e => e.Tf == 1), "todavía no se dispara");
        M(m, "09:36", 101.2, 101.3, 99, 99.5);
        CisdEvento ev = m.NuevosCisd.FirstOrDefault(e => e.Tf == 1);
        Afirmar(ev != null && ev.Dir == -1 && ev.Lvl == 100 && ev.TStart == H("09:31") && ev.T == H("09:37"), "CISD bajista al cierre de 09:36");
        Afirmar(!cl.Armado && m.Ltf[1].UltBaj == ev, "se desarma y queda como el último bajista");
        Afirmar(m.CisdCandidatos(new[] { false, true, false, false, false, false }).Contains(ev), "se dibuja");
    }

    // ── vista previa ────────────────────────────────────────────────────────

    static void VistaPrevia()
    {
        Afirmar(Motor.FinVelaAbierta(H("09:34"), 1) == H("09:35"), "1m abierta a las 09:34 cierra 09:35");
        Afirmar(Motor.FinVelaAbierta(H("09:30"), 5) == H("09:35") && Motor.FinVelaAbierta(H("09:34"), 5) == H("09:35"), "5m cierra 09:35");
        Afirmar(Motor.FinVelaAbierta(H("09:34"), 3) == H("09:36"), "3m de 09:33 cierra 09:36");

        bool[] solo1 = { false, true, false, false, false, false };
        Motor m = MotorConFvgAlcista(null);
        M(m, "09:33", 105.5, 106, 102.5, 103);
        var pi = new List<Fvg>();
        var pc = new List<CisdEvento>();
        m.Previa(100.5, H("09:34"), solo1, 10, 2, new List<Fvg>(), new List<CisdEvento>(), pi, pc);
        Afirmar(pi.Any(f => f.Bot == 101 && f.Top == 103), "precio bajo 101: el IFVG se ve en formación");
        m.Previa(102, H("09:34"), solo1, 10, 2, new List<Fvg>(), new List<CisdEvento>(), pi, pc);
        Afirmar(pi.Count == 0, "precio de vuelta en 102: desaparece");

        m = MotorConMaximo();
        m.Previa(99.8, H("09:36"), solo1, 10, 2, new List<Fvg>(), new List<CisdEvento>(), pi, pc);
        Afirmar(pc.Any(e => e.Dir == -1 && e.Lvl == 100 && e.T == H("09:37")), "CISD bajista en formación hasta 09:37");
        m.Previa(100.2, H("09:36"), solo1, 10, 2, new List<Fvg>(), new List<CisdEvento>(), pi, pc);
        Afirmar(pc.Count(e => e.Dir == -1) == 0, "sobre el nivel: no hay CISD en formación");
    }

    // ── FVG de 15m ──────────────────────────────────────────────────────────

    static void Bloque15(Motor m, string inicio, double o, double h, double l, double c)
    {
        for (int i = 0; i < 15; i++)
            M(m, H(inicio).AddMinutes(i), i == 0 ? o : c, h, l, c);
    }

    static void FvgQuinceMinutos()
    {
        var m = new Motor(new ConfigMotor());
        Bloque15(m, "09:30", 100.5, 101, 100, 100.8);
        Bloque15(m, "09:45", 101, 106, 101, 105.5);
        Bloque15(m, "10:00", 105.5, 107, 104, 106);
        FvgHtf f = m.Htf15.FirstOrDefault(x => x.Bot == 101 && x.Top == 104);
        Afirmar(f != null && f.Dir == 1 && f.Vivo && f.TBirth == H("09:30"), "FVG alcista de 15m 101–104 desde 09:30");
        Afirmar(m.HtfSeleccion(106, true, false, false, 1).Contains(f), "es el más cercano por abajo");
        Afirmar(!m.HtfSeleccion(106, false, true, true, 1).Contains(f), "con 15m apagado no se selecciona");
        Bloque15(m, "10:15", 106, 106, 100, 103);
        Afirmar(f.Vivo, "cierre de 15m en 103 (dentro) no lo mata");
        Bloque15(m, "10:30", 103, 103, 100, 100.5);
        Afirmar(!f.Vivo, "cierre de 15m bajo 101 lo mata");
    }

    // ── liquidez ────────────────────────────────────────────────────────────

    static void LiquidezDiaYSesion()
    {
        var m = new Motor(new ConfigMotor());
        M(m, "09:30", 100, 110, 95, 105);
        M(m, "12:00", 105, 112, 104, 108);
        M(m, "16:59", 108, 109, 90, 100);
        M(m, H("18:00"), 100, 101, 99, 100);                   // nuevo día de trading
        Nivel pdh = m.Liq.Niveles.FirstOrDefault(L => L.Tipo == "PDH");
        Nivel pdl = m.Liq.Niveles.FirstOrDefault(L => L.Tipo == "PDL");
        Afirmar(pdh != null && pdh.P == 112 && pdh.Lado == 1, "PDH 112");
        Afirmar(pdl != null && pdl.P == 90 && pdl.Lado == -1, "PDL 90");
        Afirmar(m.DayStart == H("18:00"), "el día empieza a las 18:00");
        M(m, H("19:00"), 100, 103, 98, 101);
        M(m, H("00:59", 1), 101, 104, 97, 102);
        M(m, H("01:00", 1), 102, 102.5, 101.5, 102);
        Nivel ah = m.Liq.Niveles.FirstOrDefault(L => L.Tipo == "ASIA_H");
        Nivel al = m.Liq.Niveles.FirstOrDefault(L => L.Tipo == "ASIA_L");
        Afirmar(ah != null && ah.P == 104 && al != null && al.P == 97, "Asia H 104 / L 97 al cerrar la sesión");
        Afirmar(!ah.Tomado && !pdh.Tomado, "sin barrer todavía");
        M(m, H("02:00", 1), 102, 104.5, 101, 103);
        Afirmar(ah.Tomado && !pdh.Tomado, "la mecha sobre 104 barre Asia H y lo oculta");
        M(m, H("18:00", 1), 103, 103, 102, 102.5);
        Afirmar(pdh.Oculto && m.Liq.Niveles.Count(L => L.Tipo == "PDH" && !L.Oculto) == 1, "al día siguiente se reemplaza el PDH");
    }

    static void LiquidezSwings()
    {
        var m = new Motor(new ConfigMotor { EqTolPuntos = 1, SepAtr = 0.5 });
        m.Liq.AgregarSwing(100, 1, false, H("09:30"), double.NaN, "SW5_H");
        m.Liq.AgregarSwing(100.5, 1, false, H("10:00"), double.NaN, "SW5_H");
        Afirmar(m.Liq.Niveles.Count == 1 && m.Liq.Niveles[0].Tipo == "EQH" && m.Liq.Niveles[0].P == 100.5, "dos máximos a 0,5 puntos: EQH en 100,5");
        m.Liq.AgregarSwing(105, 1, false, H("10:30"), 20, "SW5_H");
        Afirmar(m.Liq.Niveles.Count == 1 && m.Liq.Niveles[0].P == 105, "a menos de 0,5 ATR: se queda el más alto");
        m.Liq.AgregarSwing(130, 1, false, H("11:00"), 20, "SW5_H");
        Afirmar(m.Liq.Niveles.Count == 2, "lejos: nivel nuevo");
        m.Liq.AgregarSwing(90, -1, false, H("11:00"), 20, "SW5_L");
        Afirmar(m.Liq.Niveles.Count == 3 && m.Liq.Niveles[2].Lado == -1, "mínimos y máximos no se mezclan");
    }

    // ── deduplicación y sesiones ────────────────────────────────────────────

    static void Deduplicacion()
    {
        var f1 = new Fvg { Tf = 1, Dir = 1, Bot = 101, Top = 103, TInv = H("09:34") };
        var g3 = new Fvg { Tf = 3, Dir = 1, Bot = 102, Top = 104, TInv = H("09:36") };
        var lista = new List<Fvg> { f1, g3 };
        Afirmar(Motor.TapadoIfvg(f1, f1.TInv, lista, g => g.TInv, 10), "un 3m que solapa tapa al 1m");
        Afirmar(!Motor.TapadoIfvg(g3, g3.TInv, lista, g => g.TInv, 10), "el 3m no queda tapado");
        g3.TInv = H("10:00");
        Afirmar(!Motor.TapadoIfvg(f1, f1.TInv, lista, g => g.TInv, 10), "fuera de la ventana no tapa");
        g3.TInv = H("09:36"); g3.Dir = -1;
        Afirmar(!Motor.TapadoIfvg(f1, f1.TInv, lista, g => g.TInv, 10), "dirección contraria no tapa");

        var c1 = new CisdEvento { Tf = 1, Dir = -1, Lvl = 100, T = H("09:37") };
        var c5 = new CisdEvento { Tf = 5, Dir = -1, Lvl = 101.5, T = H("09:40") };
        Afirmar(Motor.TapadoCisd(c1, new List<CisdEvento> { c1, c5 }, 10, 2), "CISD 5m a 1,5 puntos tapa al de 1m");
        Afirmar(!Motor.TapadoCisd(c1, new List<CisdEvento> { c1, c5 }, 10, 1), "a más de la tolerancia no tapa");
    }

    static void Sesiones()
    {
        int a, b;
        Afirmar(Utiles.LeerSesion("1900-0100", out a, out b) && a == 1140 && b == 60, "1900-0100");
        Afirmar(!Utiles.LeerSesion("19:00-01:00", out a, out b) && !Utiles.LeerSesion("", out a, out b), "formato inválido");
        Afirmar(Utiles.EnSesion(1200, 1140, 60) && Utiles.EnSesion(30, 1140, 60) && !Utiles.EnSesion(60, 1140, 60), "sesión que cruza la medianoche");
    }

    // ── estrés ──────────────────────────────────────────────────────────────

    static void Estres()
    {
        var rnd = new Random(7);
        var m = new Motor(new ConfigMotor());
        bool[] on = { false, true, true, true, true, true };
        double p = 25000;
        int nIfvg = 0, nCisd = 0;
        var pi = new List<Fvg>();
        var pc = new List<CisdEvento>();
        DateTime t = new DateTime(2026, 10, 25, 18, 0, 0);          // domingo 18:00
        DateTime fin = new DateTime(2026, 10, 30, 17, 0, 0);
        while (t < fin)
        {
            int min = t.Hour * 60 + t.Minute;
            bool pausa = min >= 17 * 60 && min < 18 * 60;
            if (!pausa && rnd.NextDouble() > 0.03)                    // 3 % de minutos sin datos
            {
                double o = p, c = Math.Round((p + (rnd.NextDouble() - 0.5) * 12) * 4) / 4;
                double h = Math.Max(o, c) + Math.Round(rnd.NextDouble() * 12) / 4, l = Math.Min(o, c) - Math.Round(rnd.NextDouble() * 12) / 4;
                m.ProcesarMinuto(t, t.AddMinutes(1), o, h, l, c);
                nIfvg += m.NuevosIfvg.Count; nCisd += m.NuevosCisd.Count;
                m.DescartarEventos();
                p = c;
                List<Fvg> conf = m.IfvgCandidatos(t.AddMinutes(1), on, true, 0);
                List<CisdEvento> cc = m.CisdCandidatos(on);
                m.Previa(c + 3, t.AddMinutes(1), on, 10, 2, conf, cc, pi, pc);
                m.HtfSeleccion(c, true, true, true, 3);
            }
            t = t.AddMinutes(1);
        }
        Afirmar(nIfvg > 50 && nCisd > 50, "hay IFVG y CISD (" + nIfvg + " / " + nCisd + ")");
        Afirmar(Enumerable.Range(1, 5).All(n => m.Ltf[n].Fvgs.Count <= 40 + 5), "FVG por TF acotados");
        Afirmar(m.Liq.Niveles.Count <= 80 && m.Htf15.Count <= 20 && m.A15.Velas.Count <= 400, "niveles y velas acotados");
        Afirmar(m.Liq.Niveles.Any(L => L.Tipo == "PDH") && m.Liq.Niveles.Any(L => L.Tipo.StartsWith("SW")), "hay PDH y swings");
        Afirmar(m.Htf240.Count > 0 || m.A240.Velas.Count >= 20, "velas de 4h");
    }

    // ── GEX ─────────────────────────────────────────────────────────────────

    static TimeZoneInfo Ny()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
    }

    static readonly DateTime AhoraUtc = new DateTime(2026, 10, 23, 14, 0, 0);   // 10:00 NY

    static GexResultado Gex(string archivo)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "datos", archivo));
        var ops = new List<GexOpcion>();
        double spot = GexCalc.Leer(json, AhoraUtc, Ny(), 45, ops);
        Afirmar(ops.Count == 126, "126 opciones (sin la vencida ni la de OI 0): " + ops.Count);
        return GexCalc.Calcular(spot, ops, 0.04, 0.006, 3);
    }

    static void GexCadena(string archivo)
    {
        // valores de gex/gex_nasdaq.py con la misma cadena (generar_cadena.py)
        GexResultado r = Gex(archivo);
        Igual(750, r.Spot, 0, "spot");
        Igual(760, r.CallWall, 0, "call wall");
        Igual(740, r.PutWall, 0, "put wall");
        Igual(756.4876320467371, r.Flip, 1e-7, "gamma flip");
        Igual(-66155916.56707895, r.Total, 1e-3, "GEX total");
        Afirmar(r.Extra.Count == 0, "sin strikes extra con OI parejo");
    }

    static void GexExtra()
    {
        GexResultado r = Gex("cadena_qqq_con_extra.json");
        Igual(747.7427516747593, r.Flip, 1e-7, "gamma flip");
        Igual(39215571.35004617, r.Total, 1e-3, "GEX total");
        Afirmar(r.Extra.SequenceEqual(new[] { 720.0, 770.0, 780.0 }), "extra 720, 770, 780: " + string.Join(", ", r.Extra));
    }

    static void GexSinPrecio()
    {
        bool lanzo = false;
        try { GexCalc.Leer("{\"data\":{\"options\":[]}}", AhoraUtc, Ny(), 45, new List<GexOpcion>()); }
        catch (FormatException) { lanzo = true; }
        Afirmar(lanzo, "FormatException");
    }
}
