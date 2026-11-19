// Simulador local do lote enviado à Route Optimization API. Não chama a Google.
// Recalcula o PenaltyCost com a fórmula de EforceParaGmproOrchestrator.CalcularPesos e resolve
// as rotas com uma heurística de inserção gulosa sobre o mesmo modelo de custo do request.
// Os números absolutos diferem da resposta real; a comparação válida é entre cenários simulados.
import fs from "fs";
import path from "path";

// Deslocamento calibrado nas transições reais do OutputESS (distância de rota / linha reta e velocidade média).
const FATOR_ROTA = 1.38;
const VELOCIDADE_KMH = 30.2;
const VELOCIDADE_MS = VELOCIDADE_KMH / 3.6;

const ts = (s) => Date.parse(s) / 1000;
const secs = (s) => parseInt(s ?? "0", 10);
const latLng = (w) => w.location.latLng;
const haversine = (a, b) => {
  const R = 6371000, r = Math.PI / 180;
  const dLat = (b.latitude - a.latitude) * r, dLng = (b.longitude - a.longitude) * r;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * r) * Math.cos(b.latitude * r) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(h));
};
const num = (label, key) => {
  const m = label.match(new RegExp(key + "\\s*(-?[\\d.]+)"));
  return m ? Number(m[1]) : 0;
};
const r1 = (n) => Math.round(n * 10) / 10;
const quant = (arr) => {
  const s = arr.filter(Number.isFinite).sort((a, b) => a - b);
  if (!s.length) return null;
  const p = (k) => s[Math.min(s.length - 1, Math.floor(k * (s.length - 1)))];
  return { n: s.length, min: r1(s[0]), p50: r1(p(0.5)), p90: r1(p(0.9)), max: r1(s.at(-1)) };
};
const tally = (arr, fn) => arr.reduce((m, x) => ((m[fn(x)] = (m[fn(x)] || 0) + 1), m), {});

const lerArquivos = (dataDir, arquivos = {}) => {
  const achar = (prefixo, explicito) => {
    const nome = explicito ?? fs.readdirSync(dataDir).find((f) => f.startsWith(prefixo) && f.endsWith(".json"));
    if (!nome) throw new Error(`Arquivo ${prefixo}*.json não encontrado em ${dataDir}`);
    return JSON.parse(fs.readFileSync(path.join(dataDir, nome), "utf8"));
  };
  const txt = fs.readFileSync(path.join(dataDir, arquivos.pesos ?? "PesosManuais.txt"), "utf8");
  return {
    input: achar("InputESS", arquivos.input),
    output: achar("OutputESS", arquivos.output),
    exec: achar("Executado", arquivos.executado)[0].ResultadoOtimizacao,
    base: Object.fromEntries([...txt.matchAll(/"(\w+)":\s*"([^"]*)"/g)].map(([, k, v]) => [k, Number(v)])),
  };
};

export const criarSimulador = (dataDir, arquivos) => {
  const { input, output, exec, base } = lerArquivos(dataDir, arquivos);

  // ---------- OS a partir do request ----------
  const todosVeiculos = input.model.vehicles.map((_, i) => i);
  const ordens = input.model.shipments.map((s, idx) => {
    const l = s.label;
    const inicioTurno = (l.match(/Inicio Turno:\s*(\S+)/) || [])[1];
    const fimTurno = (l.match(/Fim Turno:\s*(\S+)/) || [])[1];
    return {
      idx,
      id: (l.match(/^[0-9a-f]{24}/i) || [""])[0],
      // "Data Vencimento Real" sai no label em horário local (UTC-3) com sufixo Z.
      vencimentoReal: ts((l.match(/Data Vencimento Real:\s*(\S+)/) || [])[1] ?? "") + 3 * 3600,
      tipo: (l.match(/^[0-9a-f]+-([CTX])/i) || [])[1] ?? "C",
      regulada: !/NAO REGULADA/.test(l),
      riscoVida: !/NAO RISCO/.test(l),
      clienteEssencial: false,
      reincidente: /Reincidente:\s*S/.test(l),
      pessoas: num(l, "Afeta"),
      horasAberta: num(l, "Aberta a:"),
      compensacao: num(l, "Valor de Compensacao:"),
      compensacaoHora: num(l, "Valor de Compensacao Por Hora:"),
      vencida: /VENCIDA/.test(l),
      horasAteVencer: num(l, "Tempo ate vencer:"),
      prioridade: (l.match(/Prioridade:\s*([A-Z]+)/) || [])[1] ?? "NORMAL",
      inicioTurno: inicioTurno ? ts(inicioTurno) : null,
      fimTurno: fimTurno ? ts(fimTurno) : null,
      penaltyReal: Number(s.penaltyCost ?? 0),
      permitidos: s.allowedVehicleIndices?.length ? s.allowedVehicleIndices : todosVeiculos,
      deliveries: s.deliveries,
      local: latLng(s.deliveries[0].arrivalWaypoint),
      duracao: secs(s.deliveries[0].duration),
      carga: Number(s.loadDemands?.NUMERO_VISITAS?.amount ?? 1),
    };
  });
  for (const o of ordens) o.horasVencida = Math.trunc((o.inicioTurno - o.vencimentoReal) / 3600);

  // ---------- Reprodução de EforceParaGmproOrchestrator.CalcularPesos ----------
  const minMax = (xs) => {
    const lo = Math.min(...xs), hi = Math.max(...xs);
    return hi === lo ? xs.map(() => 0) : xs.map((x) => (x - lo) / (hi - lo));
  };
  const valorPrioridade = (p, c) => ({ URGENTE: c.PrioridadeUrgente, ALTA: c.PrioridadeAlta })[p] ?? c.PrioridadeNormal;
  const diasCalendario = (o) => {
    const inicio = new Date(o.inicioTurno * 1000);
    const venc = new Date(o.vencimentoReal * 1000);
    const d = (Date.UTC(venc.getUTCFullYear(), venc.getUTCMonth(), venc.getUTCDate()) -
      Date.UTC(inicio.getUTCFullYear(), inicio.getUTCMonth(), inicio.getUTCDate())) / 86400000;
    return Math.max(1, d + 1);
  };

  const calcularPenalties = (c) => {
    const f = {
      risco: ordens.map((o) => (o.riscoVida ? c.PesoRiscoVida : 0)),
      essencial: ordens.map((o) => (o.clienteEssencial ? c.PesoClienteEssencial : 0)),
      pessoas: ordens.map((o) => (o.tipo === "T" ? c.PesoQtdPessoasAtingidas * (o.pessoas > 0 ? o.pessoas : 1) : 0)),
      compensacao: ordens.map((o) => {
        const v = o.compensacao + o.compensacaoHora;
        const isComp = o.compensacao > 0 && !o.riscoVida;
        return isComp && !o.vencida && o.tipo === "C" ? v / diasCalendario(o) : v;
      }),
      prazo: ordens.map((o) => o.horasVencida * (o.regulada ? c.PesoAtrasoCompensacaoPorHora : c.PesoAtrasoCompensacaoNReguladaPorHora)),
      regulada: ordens.map((o) => (o.regulada && !o.riscoVida && !o.clienteEssencial ? c.PesoRegulada : 0)),
      lote: ordens.map((o) => (o.tipo === "X" ? 1 : 0)),
      tecnica: ordens.map((o) => (o.tipo === "T" ? 1 : 0)),
      reincidencia: ordens.map((o) => (o.reincidente ? c.PesoReicidente : 0)),
      idadeTecnica: ordens.map((o) => (o.tipo === "T" ? c.PesoIdadeOsTecnica * o.horasAberta : 0)),
      prioridade: ordens.map((o) => valorPrioridade(o.prioridade, c)),
    };
    const imp = {
      risco: c.RiscoVidaImportancia, essencial: c.ClienteEssencialImportancia, pessoas: c.PessoasAtingidasImportancia,
      compensacao: c.CompensacaoImportancia, prazo: c.OsPrazoImportancia, regulada: c.PrioridadeEReguladaImportancia,
      lote: c.EDoLoteImportancia, tecnica: c.OSTecnicaImportancia, reincidencia: c.ReicidenciaImportancia,
      idadeTecnica: c.TempoTecnicaImportancia, prioridade: c.PrioridadeImportancia,
    };
    const n = Object.fromEntries(Object.entries(f).map(([k, v]) => [k, minMax(v)]));
    return ordens.map((_, i) => {
      let peso = 10;
      for (const k of Object.keys(f)) peso += n[k][i] * Math.pow(c.PotenciaPesos, imp[k]);
      const p = Math.floor(peso);
      return p === 0 ? 1 : p;
    });
  };

  // ---------- Visitas ----------
  const janelaDoRequest = (o) => o.deliveries.map((d) => {
    const tw = d.timeWindows[0];
    return {
      inicio: ts(tw.startTime), fim: ts(tw.endTime), softFim: ts(tw.softEndTime ?? tw.endTime),
      custoHoraAtraso: tw.costPerHourAfterSoftEndTime ?? 0, custo: d.cost ?? 0,
    };
  });
  const janelaVencidaCorrigida = (o, horasSoft, multaHora) => [{
    inicio: o.inicioTurno, fim: o.fimTurno, softFim: o.inicioTurno + horasSoft * 3600,
    custoHoraAtraso: multaHora, custo: 0,
  }];

  // ---------- Solver ----------
  const veiculos = input.model.vehicles.map((v, i) => ({
    i,
    inicio: ts(v.startTimeWindows[0].startTime),
    fimMax: ts(v.endTimeWindows[0].endTime),
    local: latLng(v.startWaypoint),
    custoKm: v.costPerKilometer ?? 0,
    custoFixo: v.fixedCost ?? 0,
    maxMetros: Number(v.routeDistanceLimit?.maxMeters ?? Infinity),
    custoKmAcimaSoft: v.routeDistanceLimit?.costPerKilometerAboveSoftMax ?? 0,
    capacidade: Number(v.loadLimits?.NUMERO_VISITAS?.maxLoad ?? Infinity),
    pausa: v.breakRule?.breakRequests?.[0]
      ? { cedo: ts(v.breakRule.breakRequests[0].earliestStartTime), tarde: ts(v.breakRule.breakRequests[0].latestStartTime), dur: secs(v.breakRule.breakRequests[0].minDuration) }
      : null,
  }));
  const limiteKmEquipe = Math.max(...veiculos.map((v) => v.maxMetros)) / 1000;

  const deslocamento = (a, b) => {
    const m = haversine(a, b) * FATOR_ROTA;
    return { m, s: m / VELOCIDADE_MS };
  };

  const melhorJanela = (o, chegada, cen) => {
    let melhor = null;
    for (const j of cen.janelas[o.idx]) {
      const inicio = Math.max(chegada, j.inicio);
      if (inicio > j.fim) continue;
      const custo = j.custo + Math.max(0, inicio - j.softFim) / 3600 * j.custoHoraAtraso;
      if (!melhor || custo < melhor.custo) melhor = { inicio, custo, j };
    }
    return melhor;
  };

  const avaliarRota = (v, seq, cen) => {
    if (!seq.length) return { custo: 0, metros: 0, termos: null };
    let carga = 0;
    for (const o of seq) carga += o.carga;
    if (carga > v.capacidade) return null;

    let t = v.inicio, pos = v.local, metros = 0, custoJanela = 0, custoVisita = 0, pausou = !v.pausa;
    for (const o of seq) {
      const d = deslocamento(pos, o.local);
      metros += d.m;
      if (metros > v.maxMetros) return null;
      if (!pausou && t >= v.pausa.cedo) { t += v.pausa.dur; pausou = true; }
      let melhor = melhorJanela(o, t + d.s, cen);
      if (!melhor) return null;
      if (!pausou && melhor.inicio + o.duracao > v.pausa.tarde) {
        const tPausa = Math.max(t, v.pausa.cedo);
        if (tPausa > v.pausa.tarde) return null;
        t = tPausa + v.pausa.dur;
        pausou = true;
        melhor = melhorJanela(o, t + d.s, cen);
        if (!melhor) return null;
      }
      custoJanela += melhor.custo - melhor.j.custo;
      custoVisita += melhor.j.custo;
      t = melhor.inicio + o.duracao;
      pos = o.local;
    }
    if (!pausou) {
      const tPausa = Math.max(t, v.pausa.cedo);
      if (tPausa > v.pausa.tarde) return null;
      t = tPausa + v.pausa.dur;
    }
    if (t > v.fimMax) return null;

    const km = metros / 1000;
    const termos = {
      fixo: cen.custoFixo ?? v.custoFixo,
      porKm: km * (cen.custoKm ?? v.custoKm),
      acimaSoft: Math.max(0, km - cen.softMaxMetros / 1000) * v.custoKmAcimaSoft,
      janela: custoJanela,
      visita: custoVisita,
    };
    return { custo: termos.fixo + termos.porKm + termos.acimaSoft + termos.janela + termos.visita, metros, termos };
  };

  const resolver = (cen) => {
    const rotas = veiculos.map(() => []);
    const custoRota = veiculos.map(() => 0);
    const pendentes = new Set(ordens.map((o) => o.idx));

    const melhorInsercao = (o, vi) => {
      const v = veiculos[vi], seq = rotas[vi];
      let melhor = null;
      for (let p = 0; p <= seq.length; p++) {
        const r = avaliarRota(v, [...seq.slice(0, p), o, ...seq.slice(p)], cen);
        if (!r) continue;
        const delta = r.custo - custoRota[vi];
        if (!melhor || delta < melhor.delta) melhor = { delta, p, vi };
      }
      return melhor;
    };

    const cache = new Map();
    const melhorPara = (o) => {
      let melhor = null;
      for (const vi of o.permitidos) {
        const k = o.idx * 1000 + vi;
        if (!cache.has(k)) cache.set(k, melhorInsercao(o, vi));
        const c = cache.get(k);
        if (c && (!melhor || c.delta < melhor.delta)) melhor = c;
      }
      return melhor;
    };

    for (;;) {
      let escolha = null;
      for (const idx of pendentes) {
        const m = melhorPara(ordens[idx]);
        if (!m) continue;
        const ganho = cen.penalties[idx] - m.delta;
        if (ganho > 0 && (!escolha || ganho > escolha.ganho)) escolha = { ...m, idx, ganho };
      }
      if (!escolha) break;
      rotas[escolha.vi].splice(escolha.p, 0, ordens[escolha.idx]);
      custoRota[escolha.vi] = avaliarRota(veiculos[escolha.vi], rotas[escolha.vi], cen).custo;
      pendentes.delete(escolha.idx);
      for (const idx of pendentes) cache.delete(idx * 1000 + escolha.vi);
    }
    return { rotas, pendentes };
  };

  // ---------- Métricas ----------
  const grupoPorId = new Map();
  for (const e of exec.Equipes) for (const os of e.OrdensServico) if (!os.IsAlmoco) grupoPorId.set(os._id, e.Aprovada ? "aprovada" : "planejada_nao_aprovada");
  for (const os of exec.OsNaoAtendidas) grupoPorId.set(os._id, "fora_da_rota");
  const servidosGoogle = new Set(output.routes.flatMap((r) => (r.visits || []).map((v) => v.shipmentIndex ?? 0)));

  const medir = (cen, sol) => {
    const atendidas = new Set(sol.rotas.flat().map((o) => o.idx));
    const custos = { penalty: 0, fixo: 0, porKm: 0, acimaSoft: 0, janela: 0, visita: 0 };
    for (const idx of sol.pendentes) custos.penalty += cen.penalties[idx];
    const rotasAtivas = [];
    sol.rotas.forEach((seq, vi) => {
      const r = avaliarRota(veiculos[vi], seq, cen);
      if (!r?.termos) return;
      for (const k of Object.keys(r.termos)) custos[k] += r.termos[k];
      rotasAtivas.push({ visitas: seq.length, km: r.metros / 1000 });
    });
    const fora = [...sol.pendentes].map((i) => ordens[i]);
    const dentro = [...atendidas].map((i) => ordens[i]);
    return {
      atendidas: atendidas.size,
      foraDaRota: sol.pendentes.size,
      vencidasAtendidas: dentro.filter((o) => o.vencida).length,
      vencidasForaDaRota: fora.filter((o) => o.vencida).length,
      veiculosUsados: rotasAtivas.length,
      custos: Object.fromEntries(Object.entries(custos).map(([k, v]) => [k, Math.round(v)])),
      custoTotal: Math.round(Object.values(custos).reduce((a, b) => a + b, 0)),
      kmTotal: r1(rotasAtivas.reduce((a, r) => a + r.km, 0)),
      rotas: { km: quant(rotasAtivas.map((r) => r.km)), visitas: quant(rotasAtivas.map((r) => r.visitas)) },
      penaltyDasForaDaRota: quant(fora.map((o) => cen.penalties[o.idx])),
      penaltyDasAtendidas: quant(dentro.map((o) => cen.penalties[o.idx])),
      atendidasPorGrupoReal: tally(dentro, (o) => grupoPorId.get(o.id) ?? "sem_correspondencia"),
      sobreposicaoComGoogle: dentro.filter((o) => servidosGoogle.has(o.idx)).length,
      horasAteVencerDasAtendidas: quant(dentro.filter((o) => !o.vencida).map((o) => o.horasAteVencer)),
    };
  };

  // Parâmetros de um cenário:
  // { pesos, softMaxMetros, corrigirVencida, horasSoftVencida, multaVencidaHora, custoKmEquipe, custoFixoEquipe }
  const simularSemRegras = (p) => {
    const pesos = { ...base, ...(p.pesos ?? {}) };
    const cen = {
      softMaxMetros: p.softMaxMetros ?? base.MetrosLimiteRotaPorEquipeSoft,
      custoKm: p.custoKmEquipe,
      custoFixo: p.custoFixoEquipe,
      penalties: calcularPenalties(pesos),
      janelas: ordens.map((o) => (p.corrigirVencida && o.vencida
        ? janelaVencidaCorrigida(o, p.horasSoftVencida ?? 4, p.multaVencidaHora ?? pesos.MultaAtrasoReguladaHora)
        : janelaDoRequest(o))),
    };
    return medir(cen, resolver(cen));
  };

  const referencia = simularSemRegras({});

  // Regras de frota obrigatórias (guidelines 6.4). R2 compara simulado com simulado.
  const regras = (m) => {
    const r1 = { equipesUsadas: m.veiculosUsados, equipesDisponiveis: veiculos.length, ok: m.veiculosUsados <= veiculos.length };
    const maiorKm = m.rotas.km?.max ?? 0;
    const r2 = {
      kmTotal: m.kmTotal, kmReferencia: referencia.kmTotal,
      maiorKmEquipe: maiorKm, limiteKmEquipe,
      ok: m.kmTotal <= referencia.kmTotal && maiorKm <= limiteKmEquipe,
    };
    return { r1, r2, ok: r1.ok && r2.ok };
  };

  const simular = (p) => {
    const m = p && Object.keys(p).length ? simularSemRegras(p) : referencia;
    return { ...m, regras: regras(m) };
  };

  const penaltiesBase = calcularPenalties(base);
  const confere = ordens.filter((o, i) => Math.abs(penaltiesBase[i] - o.penaltyReal) <= 1).length;
  const calibracao = {
    fatorRota: FATOR_ROTA,
    velocidadeKmH: VELOCIDADE_KMH,
    penaltyReconstruidoIgualAoRequest: `${confere}/${ordens.length}`,
    googleReal: {
      atendidas: servidosGoogle.size,
      foraDaRota: ordens.length - servidosGoogle.size,
      vencidasAtendidas: ordens.filter((o) => o.vencida && servidosGoogle.has(o.idx)).length,
      veiculosUsados: output.metrics.usedVehicleCount,
      kmTotal: r1(output.metrics.aggregatedRouteMetrics.travelDistanceMeters / 1000),
      custoTotal: Math.round(output.metrics.totalCost),
    },
  };

  return { cenario: input.label, base, simular, calibracao, totalOs: ordens.length, totalVencidas: ordens.filter((o) => o.vencida).length };
};
