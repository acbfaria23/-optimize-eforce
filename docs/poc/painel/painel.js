const NS = "http://www.w3.org/2000/svg";
const fmt = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 1 });
const n = (v) => (v == null ? "—" : fmt.format(v));

const GRUPOS = [
  { chave: "aprovada", nome: "Em equipe aprovada", cor: "var(--aprovada)" },
  { chave: "planejada_nao_aprovada", nome: "Em rota não aprovada", cor: "var(--planejada)" },
  { chave: "fora_da_rota", nome: "Fora da rota", cor: "var(--fora)" },
];

const CUSTOS = {
  "model.shipments.penalty_cost": "Pular OS (penalty)",
  "model.vehicles.route_distance_limit.cost_per_kilometer_above_soft_max": "Km acima do soft max",
  "model.vehicles.cost_per_kilometer": "Custo por km",
  "model.vehicles.fixed_cost": "Custo fixo",
};

const CORRELACOES = {
  compensacao: "Compensação",
  compensacaoHora: "Compensação por hora",
  horasAteVencer: "Horas até vencer",
  vencida: "Vencida",
  horasAberta: "Horas aberta",
};

const CENARIOS = {
  atual: "Parâmetros do dia",
  soft20km: "Só soft max 20 km",
  prazo6_potencia5: "Só OsPrazoImportancia 6",
  janela_vencida: "Só janela da vencida",
  prazo6_soft20: "Prazo 6, 20 km",
  pesos_sugeridos: "Potência 2, prazo 6, 20 km",
  sugerido_completo: "Potência 2, prazo 6, 20 km, janela",
  revisado: "Potência 5, prazo 6, 20 km, janela",
  otimizado: "Otimizado (busca automática)",
};

const PARAMETROS_BUSCA = {
  PotenciaPesos: "PotenciaPesos",
  OsPrazoImportancia: "OsPrazoImportancia",
  CompensacaoImportancia: "CompensacaoImportancia",
  MetrosLimiteRotaPorEquipeSoft: "MetrosLimiteRotaPorEquipeSoft (m)",
  CustoKmEquipe: "CustoKmEquipe",
  janelaVencidaCorrigida: "Janela da vencida corrigida",
  horasSoftVencida: "Soft end da vencida (h após início do turno)",
  multaVencidaHora: "Multa por hora da vencida após o soft end",
};
const valorParametro = (v) => (v === true ? "sim" : v === false ? "não" : n(v));

const el = (tag, attrs = {}, ...filhos) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === "class") e.className = v;
    else if (k === "style") e.style.cssText = v;
    else e.setAttribute(k, v);
  }
  for (const f of filhos.flat()) if (f != null) e.append(f instanceof Node ? f : String(f));
  return e;
};

const svg = (tag, attrs = {}, texto) => {
  const e = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
  if (texto != null) e.textContent = texto;
  return e;
};

const carregar = async (arquivo) => {
  if (window.POC_DADOS?.[arquivo]) return window.POC_DADOS[arquivo];
  const r = await fetch(`../${arquivo}`);
  if (!r.ok) throw new Error(`${arquivo}: HTTP ${r.status}`);
  return r.json();
};

// Barras horizontais simples. itens: [{rotulo, valor, cor, texto}]
const barras = (itens, { max, referencia, largura = 520, rotuloLargura = 210 } = {}) => {
  const alt = 26;
  const topo = 4;
  const s = svg("svg", { viewBox: `0 0 ${largura} ${itens.length * alt + topo + 8}` });
  const teto = max ?? Math.max(...itens.map((i) => i.valor), referencia?.valor ?? 0) * 1.08;
  const area = largura - rotuloLargura - 70;
  const x = (v) => rotuloLargura + (v / teto) * area;
  itens.forEach((it, k) => {
    const y = topo + k * alt;
    s.append(svg("text", { x: rotuloLargura - 8, y: y + 15, "text-anchor": "end", class: it.destaque ? "" : "suave", "font-weight": it.destaque ? 600 : 400 }, it.rotulo));
    s.append(svg("rect", { x: rotuloLargura, y: y + 3, width: Math.max(1, x(it.valor) - rotuloLargura), height: alt - 8, rx: 3, fill: it.cor ?? "var(--destaque)", opacity: it.destaque || it.destaque === undefined ? 1 : 0.55 }));
    s.append(svg("text", { x: x(it.valor) + 6, y: y + 15 }, it.texto ?? n(it.valor)));
  });
  if (referencia) {
    const rx = x(referencia.valor);
    s.append(svg("line", { x1: rx, x2: rx, y1: 0, y2: itens.length * alt + topo, stroke: "var(--texto)", "stroke-dasharray": "4 3", opacity: 0.6 }));
    s.append(svg("text", { x: rx + 4, y: itens.length * alt + topo + 8, class: "suave", "font-size": 11 }, referencia.rotulo));
  }
  return s;
};

const renderKpis = (r) => {
  const t = r.totais;
  const vencidas = r.perfilDoLote?.prazo?.VENCIDA ?? 0;
  const vencidasFora = r.vencidasPorGrupo?.fora_da_rota ?? 0;
  const kpis = [
    { valor: t.osNoRequest, rotulo: "OS no request" },
    { valor: t.porGrupo.aprovada ?? 0, rotulo: "Em equipe aprovada" },
    { valor: t.porGrupo.fora_da_rota ?? 0, rotulo: "Fora da rota", alerta: true },
    { valor: `${vencidasFora}/${vencidas}`, rotulo: "Vencidas fora da rota", alerta: vencidasFora > 0 },
    { valor: `${t.veiculosUsados}/${t.veiculos}`, rotulo: "Veículos usados" },
    { valor: t.rotasSemVisita, rotulo: "Rotas sem visita", alerta: t.rotasSemVisita > 0 },
  ];
  document.getElementById("kpis").replaceChildren(
    ...kpis.map((k) => el("div", { class: `kpi${k.alerta ? " alerta" : ""}` }, el("div", { class: "valor" }, typeof k.valor === "number" ? n(k.valor) : k.valor), el("div", { class: "rotulo" }, k.rotulo))),
  );
};

const renderGrupos = (r) => {
  const total = r.totais.osNoRequest;
  const s = svg("svg", { viewBox: "0 0 1000 44" });
  let x = 0;
  for (const g of GRUPOS) {
    const v = r.totais.porGrupo[g.chave] ?? 0;
    const w = (v / total) * 1000;
    s.append(svg("rect", { x, y: 0, width: w, height: 30, fill: g.cor }));
    if (w > 60) s.append(svg("text", { x: x + w / 2, y: 20, "text-anchor": "middle", fill: "#fff", style: "fill:#fff;font-weight:600" }, `${v} (${Math.round((v / total) * 100)}%)`));
    x += w;
  }
  const venc = r.vencidasPorGrupo ?? {};
  const legenda = el("div", { class: "legenda" }, GRUPOS.map((g) => el("span", { style: `--cor:${g.cor}` }, `${g.nome}${venc[g.chave] ? ` · ${venc[g.chave]} vencidas` : ""}`)));
  document.getElementById("grupos").replaceChildren(s, legenda);
};

const renderCustos = (r) => {
  const itens = Object.entries(r.custosGoogle)
    .map(([k, v]) => ({ rotulo: CUSTOS[k] ?? k, valor: v, cor: k.includes("penalty") ? "var(--fora)" : "var(--destaque)" }))
    .sort((a, b) => b.valor - a.valor);
  document.getElementById("custos").replaceChildren(barras(itens, { rotuloLargura: 170 }));
};

const renderCorrelacoes = (r) => {
  const itens = Object.entries(r.correlacaoComPenalty).map(([k, v]) => ({ rotulo: CORRELACOES[k] ?? k, valor: v }));
  const largura = 520, rotuloLargura = 170, alt = 26, meio = rotuloLargura + (largura - rotuloLargura - 40) / 2, meiaArea = (largura - rotuloLargura - 40) / 2;
  const s = svg("svg", { viewBox: `0 0 ${largura} ${itens.length * alt + 22}` });
  s.append(svg("line", { x1: meio, x2: meio, y1: 0, y2: itens.length * alt, class: "eixo" }));
  itens.forEach((it, k) => {
    const y = k * alt;
    const w = Math.abs(it.valor) * meiaArea;
    s.append(svg("text", { x: rotuloLargura - 8, y: y + 15, "text-anchor": "end", class: "suave" }, it.rotulo));
    s.append(svg("rect", { x: it.valor >= 0 ? meio : meio - w, y: y + 3, width: Math.max(1, w), height: alt - 8, rx: 3, fill: it.valor >= 0 ? "var(--destaque)" : "var(--planejada)" }));
    s.append(svg("text", { x: it.valor >= 0 ? meio + w + 6 : meio - w - 6, y: y + 15, "text-anchor": it.valor >= 0 ? "start" : "end" }, it.valor.toLocaleString("pt-BR", { minimumFractionDigits: 2 })));
  });
  for (const [v, rot] of [[-1, "-1"], [0, "0"], [1, "1"]]) s.append(svg("text", { x: meio + v * meiaArea, y: itens.length * alt + 16, "text-anchor": "middle", class: "suave", "font-size": 11 }, rot));
  document.getElementById("correlacoes").replaceChildren(s);
};

const renderPenalties = (r) => {
  const largura = 1000, rotuloLargura = 190, alt = 40;
  const lo = 1, hi = 10 ** Math.ceil(Math.log10(Math.max(...GRUPOS.map((g) => r.porGrupo[g.chave]?.penalty?.max ?? 1))));
  const x = (v) => rotuloLargura + (Math.log10(Math.max(v, lo)) / Math.log10(hi)) * (largura - rotuloLargura - 20);
  const s = svg("svg", { viewBox: `0 0 ${largura} ${GRUPOS.length * alt + 30}` });
  for (let d = 1; d <= hi; d *= 10) {
    s.append(svg("line", { x1: x(d), x2: x(d), y1: 0, y2: GRUPOS.length * alt, class: "eixo" }));
    s.append(svg("text", { x: x(d), y: GRUPOS.length * alt + 18, "text-anchor": "middle", class: "suave", "font-size": 11 }, n(d)));
  }
  GRUPOS.forEach((g, k) => {
    const q = r.porGrupo[g.chave]?.penalty;
    if (!q) return;
    const y = k * alt + alt / 2;
    s.append(svg("text", { x: rotuloLargura - 10, y: y + 4, "text-anchor": "end", class: "suave" }, `${g.nome} (${q.n})`));
    s.append(svg("line", { x1: x(q.min), x2: x(q.max), y1: y, y2: y, stroke: g.cor, "stroke-width": 2 }));
    s.append(svg("rect", { x: x(q.p25), y: y - 10, width: Math.max(2, x(q.p75) - x(q.p25)), height: 20, rx: 3, fill: g.cor, opacity: 0.35, stroke: g.cor }));
    s.append(svg("line", { x1: x(q.p50), x2: x(q.p50), y1: y - 12, y2: y + 12, stroke: g.cor, "stroke-width": 3 }));
    s.append(svg("title", {}, `mín ${n(q.min)} · 25% ${n(q.p25)} · mediana ${n(q.p50)} · 75% ${n(q.p75)} · máx ${n(q.max)}`));
  });
  document.getElementById("penalties").replaceChildren(s);
};

const renderRotas = (r) => {
  const a = r.rotas.aprovadas, na = r.rotas.naoAprovadasComVisita;
  const bloco = (titulo, chave, unidade) =>
    el("div", {},
      el("p", { class: "nota" }, titulo),
      barras([
        { rotulo: `Aprovadas (${a[chave].n})`, valor: a[chave].p50, cor: "var(--aprovada)", texto: `${n(a[chave].p50)} ${unidade}` },
        { rotulo: `Não aprovadas (${na[chave].n})`, valor: na[chave].p50, cor: "var(--planejada)", texto: `${n(na[chave].p50)} ${unidade}` },
      ], { rotuloLargura: 170 }));
  document.getElementById("rotas").replaceChildren(bloco("Km por rota (mediana)", "km", "km"), bloco("Visitas por rota (mediana)", "visitas", "visitas"));
};

const ordemCenarios = (sim) => {
  const preferida = Object.keys(CENARIOS).filter((k) => sim.cenarios[k]);
  return [...preferida, ...Object.keys(sim.cenarios).filter((k) => !preferida.includes(k))];
};

const renderSimulacao = (sim, dia, resultado) => {
  document.getElementById("avisoSimulacao").textContent = sim.aviso;
  const c = sim.calibracao;
  document.getElementById("calibracao").replaceChildren(
    el("span", { class: "selo" }, "Penalty reconstruído: ", el("b", {}, c.penaltyReconstruidoIgualAoRequest)),
    el("span", { class: "selo" }, "Rota = ", el("b", {}, `${c.fatorRota.toLocaleString("pt-BR")}× linha reta`), " a ", el("b", {}, `${n(c.velocidadeKmH)} km/h`)),
    el("span", { class: "selo" }, "Simulado com parâmetros do dia: ", el("b", {}, n(sim.cenarios.atual?.atendidas)), " atendidas · Google: ", el("b", {}, n(c.googleReal.atendidas))),
  );

  const chaves = ordemCenarios(sim);
  const rec = dia.cenarioRecomendado;

  const cab = el("tr", {}, ["Cenário", "Atendidas", "Vencidas em rota", "Veículos", "Km total", "Km por rota (mediana)", "Coincidem com a Google"].map((h, i) => el("th", { class: i ? "num" : "" }, h)));
  const g = c.googleReal;
  const linhaGoogle = el("tr", { class: "referencia" }, el("td", {}, "Google real"), el("td", { class: "num" }, n(g.atendidas)), el("td", { class: "num" }, n(g.vencidasAtendidas)), el("td", { class: "num" }, n(g.veiculosUsados)), el("td", { class: "num" }, n(g.kmTotal)), el("td", { class: "num" }, "—"), el("td", { class: "num" }, "—"));
  const linhas = chaves.map((k) => {
    const s = sim.cenarios[k];
    return el("tr", { class: k === rec ? "recomendado" : "", title: s.descricao },
      el("td", {}, CENARIOS[k] ?? k),
      el("td", { class: "num" }, n(s.atendidas)),
      el("td", { class: "num" }, n(s.vencidasAtendidas)),
      el("td", { class: "num" }, n(s.veiculosUsados)),
      el("td", { class: "num" }, n(s.kmTotal)),
      el("td", { class: "num" }, n(s.rotas?.km?.p50)),
      el("td", { class: "num" }, n(s.sobreposicaoComGoogle)));
  });
  document.getElementById("simTabela").replaceChildren(el("thead", {}, cab), el("tbody", {}, linhaGoogle, linhas));
};

const sinal = (v, casas = 0) => {
  const r = Number(v.toFixed(casas));
  return `${r > 0 ? "+" : r < 0 ? "−" : "±"}${fmt.format(Math.abs(r))}`;
};

// Diferença entre dois cenários, já classificada para os chips.
const delta = (sim, de, para) => {
  const a = sim.cenarios[de], b = sim.cenarios[para];
  if (!a || !b) return null;
  return {
    os: b.atendidas - a.atendidas,
    vencidas: b.vencidasAtendidas - a.vencidasAtendidas,
    km: b.kmTotal - a.kmTotal,
    veiculos: b.veiculosUsados - a.veiculosUsados,
  };
};

const chipsRegras = (cenario) => {
  const r = cenario?.regras;
  if (!r) return [];
  return [
    el("span", { class: `chip ${r.r1.ok ? "bom" : "ruim"}`, title: "R1: equipes usadas dentro das disponíveis" }, `R1 ${r.r1.ok ? "✓" : "✗"} ${r.r1.equipesUsadas}/${r.r1.equipesDisponiveis} equipes`),
    el("span", { class: `chip ${r.r2.ok ? "bom" : "ruim"}`, title: "R2: km total dentro do km da referência simulada e cada rota dentro do limite" },
      `R2 ${r.r2.ok ? "✓" : "✗"} ${n(Math.round(r.r2.kmTotal))} km ${r.r2.kmTotal <= r.r2.kmReferencia ? "≤" : ">"} ${n(Math.round(r.r2.kmReferencia))}`),
  ];
};

const chipsDelta = (d, cenario) => [
  el("span", { class: `chip ${d.os > 0 ? "bom" : d.os < 0 ? "ruim" : "neutro"}` }, `${sinal(d.os)} OS atendidas`),
  el("span", { class: `chip ${d.vencidas > 0 ? "bom" : d.vencidas < 0 ? "ruim" : "neutro"}` }, `${sinal(d.vencidas)} vencidas em rota`),
  el("span", { class: `chip ${Math.abs(d.km) < 1 ? "neutro" : "custo"}` }, `${sinal(d.km)} km`),
  ...chipsRegras(cenario),
];

const renderResumo = (sim, dia, resultado) => {
  const base = sim.cenarios[dia.cenarioBase ?? "atual"];
  const otim = sim.cenarios.otimizado;
  if (otim?.regras?.ok) {
    const vencidas = resultado.perfilDoLote?.prazo?.VENCIDA ?? 0;
    const r = otim.regras;
    document.getElementById("resumo").replaceChildren(
      el("p", {},
        "A melhor combinação que respeita as regras de frota atende ", el("b", {}, `${otim.atendidas} OS em vez de ${base.atendidas}`),
        " e coloca ", el("b", {}, `${otim.vencidasAtendidas} das ${vencidas} vencidas`), " em rota, ",
        `rodando ${n(Math.round(r.r2.kmTotal))} km, dentro dos ${n(Math.round(r.r2.kmReferencia))} km da referência, com ${r.r1.equipesUsadas} das ${r.r1.equipesDisponiveis} equipes.`),
      el("div", { class: "numeros" },
        el("div", {}, el("span", { class: "grande" }, sinal(otim.atendidas - base.atendidas)), el("span", { class: "de" }, "OS atendidas")),
        el("div", {}, el("span", { class: "grande" }, `${base.vencidasAtendidas} → ${otim.vencidasAtendidas}`), el("span", { class: "de" }, `vencidas em rota, de ${vencidas}`)),
        el("div", {}, el("span", { class: "grande" }, `${sinal(Math.round(((otim.kmTotal - base.kmTotal) / base.kmTotal) * 100))}%`), el("span", { class: "de" }, "km rodados (R2: até 0%)")),
        el("div", {}, el("span", { class: "grande" }, `${base.foraDaRota} → ${otim.foraDaRota}`), el("span", { class: "de" }, "OS fora da rota"))),
    );
    return;
  }
  const rec = sim.cenarios[dia.cenarioRecomendado];
  const vencidas = resultado.perfilDoLote?.prazo?.VENCIDA ?? 0;
  const qtd = (dia.parametros ?? []).filter((p) => p.status === "recomendado").length;
  const d = delta(sim, dia.cenarioBase ?? "atual", dia.cenarioRecomendado);
  const pctKm = Math.round((d.km / base.kmTotal) * 100);
  document.getElementById("resumo").replaceChildren(
    el("p", {},
      `Com ${qtd} ajustes, a simulação atende `, el("b", {}, `${rec.atendidas} OS em vez de ${base.atendidas}`),
      ` e coloca `, el("b", {}, `${rec.vencidasAtendidas} das ${vencidas} vencidas`), ` em rota, que hoje ficam todas de fora. `,
      `O preço é ${sinal(d.km)} km (${sinal(pctKm)}%) e ${sinal(d.veiculos)} veículos.`),
    el("div", { class: "numeros" },
      el("div", {}, el("span", { class: "grande" }, sinal(d.os)), el("span", { class: "de" }, "OS atendidas")),
      el("div", {}, el("span", { class: "grande" }, `${base.vencidasAtendidas} → ${rec.vencidasAtendidas}`), el("span", { class: "de" }, `vencidas em rota, de ${vencidas}`)),
      el("div", {}, el("span", { class: "grande" }, `${sinal(pctKm)}%`), el("span", { class: "de" }, "km rodados")),
      el("div", {}, el("span", { class: "grande" }, `${base.foraDaRota} → ${rec.foraDaRota}`), el("span", { class: "de" }, "OS fora da rota"))),
  );
};

const cartaoAjuste = (p, sim, dia) => {
  const longa = `${p.atual}${p.sugerido}`.length > 24;
  const efeitos = [];
  const sozinho = p.cenarioSozinho && delta(sim, dia.cenarioBase ?? "atual", p.cenarioSozinho);
  const repetido = p.cenarioAntes === (dia.cenarioBase ?? "atual") && p.cenarioDepois === p.cenarioSozinho;
  const naSequencia = !repetido && p.cenarioAntes && p.cenarioDepois && delta(sim, p.cenarioAntes, p.cenarioDepois);
  if (sozinho) efeitos.push(el("div", { class: "efeito" }, el("span", { class: "lbl" }, "Sozinho"), chipsDelta(sozinho, sim.cenarios[p.cenarioSozinho])));
  if (naSequencia) efeitos.push(el("div", { class: "efeito" }, el("span", { class: "lbl" }, p.status === "descartado" ? "Ao aplicar" : "Na sequência"), chipsDelta(naSequencia, sim.cenarios[p.cenarioDepois])));
  const rotuloTipo = { config: "configuração", codigo: "muda código" };
  return el("article", { class: `ajuste ${p.status}` },
    el("div", { class: "cab" },
      el("span", { class: "nome" }, p.chave),
      el("span", {}, el("span", { class: `tag ${p.tipo}` }, rotuloTipo[p.tipo] ?? p.tipo)),
      el("span", { class: `troca${longa ? " longa" : ""}` }, el("span", { class: "de" }, p.atual), " → ", el("span", { class: "para" }, p.sugerido)),
      el("div", { class: "efeitos" }, efeitos)),
    el("div", {},
      el("p", { class: "porque" }, el("b", {}, "Por quê. "), p.porque),
      p.evidencia?.length ? el("div", {}, el("b", { style: "font-size:0.85rem" }, "Evidência no dia"), el("ul", {}, p.evidencia.map((e) => el("li", {}, e)))) : null),
  );
};

const renderAjustes = (sim, dia) => {
  const ps = dia.parametros ?? [];
  document.getElementById("recomendados").replaceChildren(...ps.filter((p) => p.status === "recomendado").map((p) => cartaoAjuste(p, sim, dia)));
  document.getElementById("descartados").replaceChildren(...ps.filter((p) => p.status === "descartado").map((p) => cartaoAjuste(p, sim, dia)));
  const cab = el("tr", {}, ["Parâmetro", "Valor", "Por que fica"].map((h) => el("th", {}, h)));
  document.getElementById("mantidos").replaceChildren(el("thead", {}, cab), el("tbody", {},
    ps.filter((p) => p.status === "mantido").map((p) => el("tr", {}, el("td", {}, el("code", {}, p.chave)), el("td", {}, p.atual), el("td", {}, p.porque)))));
};

const renderSequencia = (sim, dia, resultado) => {
  const passos = (dia.sequencia ?? []).filter((p) => sim.cenarios[p.cenario]);
  const ultimo = passos.length - 1;
  const itens = (campo) => passos.map((p, i) => ({
    rotulo: p.rotulo,
    valor: sim.cenarios[p.cenario][campo],
    cor: i === 0 ? "var(--neutro)" : i === ultimo ? "var(--aprovada)" : "var(--destaque)",
    texto: i === 0
      ? n(Math.round(sim.cenarios[p.cenario][campo]))
      : `${n(Math.round(sim.cenarios[p.cenario][campo]))} (${sinal(sim.cenarios[p.cenario][campo] - sim.cenarios[passos[i - 1].cenario][campo])})`,
  }));
  const vencidas = resultado.perfilDoLote?.prazo?.VENCIDA ?? 0;
  const g = sim.calibracao.googleReal;
  const bloco = (titulo, nota, grafico) => el("div", {}, el("p", { class: "nota" }, el("b", {}, titulo), nota ? ` · ${nota}` : ""), grafico);
  document.getElementById("sequencia").replaceChildren(
    bloco("OS atendidas", `tracejado: Google real (${g.atendidas})`, barras(itens("atendidas"), { max: resultado.totais.osNoRequest, referencia: { valor: g.atendidas, rotulo: "Google" }, rotuloLargura: 190 })),
    bloco("Vencidas em rota", `de ${vencidas}`, barras(itens("vencidasAtendidas"), { max: vencidas, rotuloLargura: 190 })),
    bloco("Km total", "tracejado: limite da R2", barras(itens("kmTotal"), { rotuloLargura: 190, referencia: { valor: sim.cenarios[dia.cenarioBase ?? "atual"].kmTotal, rotulo: "R2" } })),
  );
};

const soVencida = (k) => k === "horasSoftVencida" || k === "multaVencidaHora";

const tabelaParametros = (refParams, params) => {
  const chaves = Object.keys(PARAMETROS_BUSCA).filter((k) => !(soVencida(k) && !params.janelaVencidaCorrigida));
  return el("table", { class: "troca-tabela" },
    el("thead", {}, el("tr", {}, el("th", {}, "Parâmetro"), el("th", { class: "num" }, "No dia"), el("th", { class: "num" }, "Otimizado"))),
    el("tbody", {}, chaves.map((k) => {
      const valorDia = soVencida(k) && !refParams.janelaVencidaCorrigida ? "—" : valorParametro(refParams[k]);
      const mudou = valorDia !== valorParametro(params[k]);
      return el("tr", {}, el("td", {}, el("code", {}, PARAMETROS_BUSCA[k])), el("td", { class: "num" }, valorDia), el("td", { class: `num${mudou ? " mudou" : ""}` }, valorParametro(params[k])));
    })));
};

const tabelaJustificativas = (justs) => {
  if (!justs) return el("p", { class: "nota" }, "Execução registrada antes de o histórico guardar os motivos.");
  if (!justs.length) return el("p", { class: "nota" }, "Nenhum parâmetro mudou em relação ao dia.");
  return el("table", { class: "porque-tabela" },
    el("thead", {}, el("tr", {}, el("th", {}, "Parâmetro"), el("th", {}, "Troca"), el("th", {}, "O que a troca faz"), el("th", {}, "Efeito medido"))),
    el("tbody", {}, justs.map((j) => el("tr", {},
      el("td", {}, el("code", {}, PARAMETROS_BUSCA[j.parametro] ?? j.parametro)),
      el("td", { class: "troca" }, `${valorParametro(j.de)} → ${valorParametro(j.para)}`),
      el("td", {}, j.mecanismo),
      el("td", { class: j.efeito.semTroca.regrasOk ? "" : "segura-regra" }, j.porque)))));
};

const textoEstrategia = (est, cob) => [
  est && `até ${est.raio} parâmetros por mutação, ${Math.round(est.fracaoAleatoria * 100)}% sorteadas do zero, partindo das ${est.partidas} melhores${est.estagnacao ? ` (${est.estagnacao} rodada${est.estagnacao > 1 ? "s" : ""} seguidas sem melhora)` : ""}`,
  cob && `${n(cob.testadas)} de ${n(cob.espaco)} combinações já testadas no lote`,
].filter(Boolean).join(" · ");

const mudancas = (params, refParams) =>
  Object.keys(PARAMETROS_BUSCA)
    .filter((k) => params[k] !== refParams[k] && !(soVencida(k) && !params.janelaVencidaCorrigida))
    .map((k) => `${k} ${valorParametro(params[k])}`).join(" · ") || "nenhuma";

const tabelaRanking = (ranking, refParams) => el("table", {},
  el("thead", {}, el("tr", {}, ["#", "OS", "Vencidas", "Km", "Equipes", "Mudanças em relação ao dia"].map((h, i) => el("th", { class: i && i < 5 ? "num" : "" }, h)))),
  el("tbody", {}, (ranking ?? []).slice(0, 5).map((c, i) => el("tr", { class: i === 0 ? "recomendado" : "" },
    el("td", {}, i + 1), el("td", { class: "num" }, c.atendidas), el("td", { class: "num" }, c.vencidasAtendidas),
    el("td", { class: "num" }, n(Math.round(c.kmTotal))), el("td", { class: "num" }, c.veiculosUsados),
    el("td", {}, mudancas(c.parametros, refParams))))));

const renderOtimizacao = (sim, dia) => {
  const o = sim.otimizacao;
  document.getElementById("otimCriterio").textContent = o?.criterio ??
    "Busca combinações de parâmetros que cumprem R1 e R2 e atendem mais OS e vencidas. Cada clique parte do melhor resultado anterior.";
  const alvo = document.getElementById("otimResultado");
  if (!o) {
    alvo.replaceChildren(el("p", { class: "nota" }, "Nenhuma busca executada ainda para este dia."));
    return;
  }
  const ref = o.referencia, best = o.melhor;
  const tabela = tabelaParametros(ref.parametros, best.parametros);

  const cen = sim.cenarios.otimizado;
  const r = cen?.regras;
  const metrica = (v, rotulo, classe = "") => el("div", { class: `metrica ${classe}` }, el("div", { class: "v" }, v), el("div", { class: "r" }, rotulo));
  const metricas = el("div", { class: "metricas" },
    metrica(`${ref.atendidas} → ${best.atendidas}`, "OS atendidas"),
    metrica(`${ref.vencidasAtendidas} → ${best.vencidasAtendidas}`, "vencidas em rota"),
    r ? metrica(`${n(Math.round(r.r2.kmTotal))} km`, `R2: até ${n(Math.round(r.r2.kmReferencia))} km · maior rota ${n(r.r2.maiorKmEquipe)} de ${n(r.r2.limiteKmEquipe)} km`, r.r2.ok ? "ok" : "falha") : null,
    r ? metrica(`${r.r1.equipesUsadas}/${r.r1.equipesDisponiveis}`, "R1: equipes usadas / disponíveis", r.r1.ok ? "ok" : "falha") : null);

  const ranking = tabelaRanking(o.ranking, ref.parametros);

  const historico = (o.historico ?? []).map((h, i) => `rodada ${i + 1}: ${h.atendidas} OS e ${h.vencidasAtendidas} vencidas${h.melhorou ? " (melhorou)" : ""}`).join(" · ");
  alvo.replaceChildren(
    el("p", { class: "nota" }, `Rodada ${o.rodadas} em ${new Date(o.executadaEm).toLocaleString("pt-BR")} · ${o.avaliacoesNestaRodada} combinações ${o.cobertura ? "novas" : ""} testadas · ${o.melhorou ? "encontrou resultado melhor" : "não superou o melhor anterior"}.`),
    o.estrategia || o.cobertura ? el("p", { class: "nota" }, `Estratégia: ${textoEstrategia(o.estrategia, o.cobertura)}. Combinações já testadas em rodadas anteriores não são repetidas.`) : null,
    metricas,
    el("div", { class: "grade" },
      el("div", {}, el("h3", {}, "Melhor combinação"), tabela),
      el("div", {}, el("h3", {}, "Outras combinações viáveis"), el("div", { class: "rolagem" }, ranking))),
    el("h3", { class: "espaco" }, "Por que cada parâmetro mudou"),
    el("p", { class: "nota" }, "Efeito medido: a mesma combinação simulada de novo com só aquele parâmetro no valor do dia."),
    tabelaJustificativas(best.justificativas),
    historico ? el("p", { class: "nota espaco" }, `Histórico: ${historico}`) : null,
  );
};

const statusOtimizacao = (texto, progresso) => {
  const alvo = document.getElementById("otimStatus");
  if (!alvo) return;
  alvo.replaceChildren(
    progresso ? el("div", { class: "progresso" }, el("div", { style: `width:${Math.round((progresso.feitas / progresso.total) * 100)}%` })) : "",
    texto ? el("p", { class: "nota" }, texto) : "",
  );
};

const acompanharOtimizacao = async () => {
  const btn = document.getElementById("btnOtimizar");
  btn.disabled = true;
  for (;;) {
    const t = await (await fetch("/api/otimizar", { cache: "no-store" })).json();
    if (t.rodando) {
      const p = t.progresso ?? {};
      const m = p.melhor;
      statusOtimizacao(`Testando combinações: ${p.feitas ?? 0}/${p.total ?? "?"}${m ? ` · melhor até agora: ${m.atendidas} OS, ${m.vencidas} vencidas, ${n(Math.round(m.km))} km` : ""}`, p.total ? p : null);
      await new Promise((ok) => setTimeout(ok, 1000));
      continue;
    }
    if (t.erro) {
      statusOtimizacao(`A busca falhou: ${t.erro}`);
      btn.disabled = false;
      return;
    }
    statusOtimizacao("Busca concluída. Atualizando o painel…");
    location.reload();
    return;
  }
};

const ligarBotaoOtimizacao = (dia) => {
  const btn = document.getElementById("btnOtimizar");
  if (location.protocol === "file:") {
    btn.disabled = true;
    statusOtimizacao("Para gerar uma nova simulação, rode node docs/poc/painel/servir.mjs e abra http://localhost:5173/painel/. A busca precisa dos dados brutos, que ficam só na sua máquina.");
    return;
  }
  btn.onclick = async () => {
    btn.disabled = true;
    statusOtimizacao("Iniciando a busca…");
    const r = await fetch(`/api/otimizar?dia=${encodeURIComponent(dia.data)}&avaliacoes=120`, { method: "POST" });
    if (!r.ok && r.status !== 409) {
      statusOtimizacao(`Não foi possível iniciar: ${(await r.json()).erro}`);
      btn.disabled = false;
      return;
    }
    acompanharOtimizacao();
  };
  fetch("/api/otimizar", { cache: "no-store" }).then((r) => r.json()).then((t) => { if (t.rodando) acompanharOtimizacao(); }).catch(() => {});
};

// ---------- Aba de histórico ----------
const pontos = (m) => m.atendidas + m.vencidasAtendidas;
const dataHora = (iso) => new Date(iso);
const doisDig = (v) => String(v).padStart(2, "0");

const graficoEvolucao = (execs) => {
  const largura = 1000, altura = 170, margem = { e: 50, d: 20, t: 14, b: 28 };
  const serie = execs.map((e) => ({ geral: e.melhorGeral.atendidas, rodada: e.melhorDaRodada?.atendidas ?? null, ref: e.referencia.atendidas }));
  const vals = serie.flatMap((s) => [s.geral, s.rodada, s.ref]).filter((v) => v != null);
  const lo = Math.floor(Math.min(...vals) * 0.95), hi = Math.ceil(Math.max(...vals) * 1.02);
  const x = (i) => margem.e + (serie.length === 1 ? (largura - margem.e - margem.d) / 2 : (i / (serie.length - 1)) * (largura - margem.e - margem.d));
  const y = (v) => margem.t + (1 - (v - lo) / (hi - lo || 1)) * (altura - margem.t - margem.b);
  const s = svg("svg", { viewBox: `0 0 ${largura} ${altura}` });
  for (const v of [lo, Math.round((lo + hi) / 2), hi]) {
    s.append(svg("line", { x1: margem.e, x2: largura - margem.d, y1: y(v), y2: y(v), class: "eixo" }));
    s.append(svg("text", { x: margem.e - 8, y: y(v) + 4, "text-anchor": "end", class: "suave", "font-size": 11 }, n(v)));
  }
  s.append(svg("line", { x1: margem.e, x2: largura - margem.d, y1: y(serie[0].ref), y2: y(serie[0].ref), stroke: "var(--neutro)", "stroke-dasharray": "4 3" }));
  s.append(svg("text", { x: largura - margem.d, y: y(serie[0].ref) - 5, "text-anchor": "end", class: "suave", "font-size": 11 }, `parâmetros do dia: ${serie[0].ref}`));
  s.append(svg("polyline", { points: serie.map((p, i) => `${x(i)},${y(p.geral)}`).join(" "), fill: "none", stroke: "var(--aprovada)", "stroke-width": 2.5 }));
  serie.forEach((p, i) => {
    if (p.rodada != null) s.append(svg("circle", { cx: x(i), cy: y(p.rodada), r: 4, fill: "var(--destaque)" }, null));
    s.append(svg("text", { x: x(i), y: altura - 8, "text-anchor": "middle", class: "suave", "font-size": 10 }, `#${i + 1}`));
  });
  return el("div", {}, s, el("div", { class: "legenda" },
    el("span", { style: "--cor:var(--aprovada)" }, "Melhor acumulado (OS atendidas)"),
    el("span", { style: "--cor:var(--destaque)" }, "Melhor encontrado em cada execução")));
};

const cartaoExecucao = (e, numero, melhorDeTodas, rep) => {
  const d = dataHora(e.executadaEm);
  const m = e.melhorDaRodada ?? e.melhorGeral;
  const ref = e.referencia;
  const resumoTopo = [
    el("span", { class: "hora" }, `${doisDig(d.getHours())}:${doisDig(d.getMinutes())}:${doisDig(d.getSeconds())}`),
    el("span", { class: "chip neutro" }, `#${numero}`),
    e.dia ? el("span", { class: "chip neutro" }, `lote ${e.dia.split("-").reverse().join("/")}`) : null,
    el("span", { class: "chip neutro" }, e.origem),
    el("span", {}, el("b", {}, m.atendidas), " OS · ", el("b", {}, m.vencidasAtendidas), " vencidas · ", `${n(Math.round(m.kmTotal))} km`),
    ...chipsRegras({ regras: m.regras }),
    e.melhorou ? el("span", { class: "chip bom" }, "melhorou o acumulado") : el("span", { class: "chip neutro" }, "não superou o acumulado"),
    melhorDeTodas ? el("span", { class: "chip bom" }, "★ melhor de todas") : null,
    rep ? el("span", { class: "chip neutro" }, `mesmo resultado que #${rep.numero}`) : null,
  ];
  const notaRepeticao = rep && el("p", { class: "nota" }, rep.difs.length
    ? `Mesmo resultado da execução #${rep.numero}, mas com outra combinação (${rep.difs.join(" · ")}). Nesta região, esses parâmetros não mudam o resultado simulado.`
    : `Mesma combinação da execução #${rep.numero}. Execuções gravadas antes da memória de combinações testadas podiam repetir candidatos.`);
  const metrica = (v, rotulo) => el("div", { class: "metrica" }, el("div", { class: "v" }, v), el("div", { class: "r" }, rotulo));
  return el("details", { class: `execucao${melhorDeTodas ? " melhor" : ""}` },
    el("summary", {}, resumoTopo),
    el("div", { class: "corpo" },
      el("p", { class: "nota" },
        `Rodada ${e.rodada} · ${e.tentativas} tentativas, ${e.combinacoesTestadas} combinações ${e.cobertura ? "inéditas" : "diferentes"}`,
        e.viaveis != null ? ` (${e.viaveis} viáveis, ${e.inviaveis} reprovadas em R1/R2)` : "",
        e.duracaoSegundos != null ? ` · ${n(e.duracaoSegundos)} s` : "",
        ` · semente ${e.semente}`,
        e.melhorDaRodada ? "" : " · nenhuma combinação nova viável; mostrando o melhor acumulado"),
      e.estrategia ? el("p", { class: "nota" }, `Estratégia: ${textoEstrategia(e.estrategia, e.cobertura)}.`) : null,
      notaRepeticao,
      el("div", { class: "metricas" },
        metrica(`${ref.atendidas} → ${m.atendidas}`, "OS atendidas"),
        metrica(`${ref.vencidasAtendidas} → ${m.vencidasAtendidas}`, "vencidas em rota"),
        metrica(`${n(Math.round(ref.kmTotal))} → ${n(Math.round(m.kmTotal))}`, "km total (R2: não pode subir)"),
        metrica(`${m.veiculosUsados}/${m.regras?.r1?.equipesDisponiveis ?? "?"}`, "equipes (R1)")),
      el("h3", {}, "Por que cada parâmetro mudou"),
      el("p", { class: "nota" }, "Efeito medido: a mesma combinação simulada de novo com só aquele parâmetro no valor do dia."),
      tabelaJustificativas(m.justificativas),
      el("div", { class: "grade espaco" },
        el("div", {}, el("h3", {}, "Parâmetros encontrados nesta execução"), tabelaParametros(ref.parametros, m.parametros)),
        el("div", {}, el("h3", {}, "Ranking desta execução"), el("div", { class: "rolagem" }, tabelaRanking(e.ranking, ref.parametros))))));
};

// Execução anterior com o mesmo resultado e quais parâmetros diferem entre as duas.
const repeticao = (execs, i) => {
  const m = execs[i].melhorDaRodada;
  if (!m) return null;
  const mesmo = (a) => a && a.atendidas === m.atendidas && a.vencidasAtendidas === m.vencidasAtendidas && Math.round(a.kmTotal) === Math.round(m.kmTotal);
  for (let j = i - 1; j >= 0; j--) {
    const a = execs[j].melhorDaRodada;
    if (!mesmo(a)) continue;
    const difs = Object.keys(PARAMETROS_BUSCA).filter((k) => valorParametro(a.parametros[k]) !== valorParametro(m.parametros[k]) && !(soVencida(k) && !m.parametros.janelaVencidaCorrigida));
    return { numero: j + 1, difs: difs.map((k) => `${k} ${valorParametro(a.parametros[k])} → ${valorParametro(m.parametros[k])}`) };
  }
  return null;
};

const renderHistorico = (hist) => {
  const alvo = document.getElementById("historico");
  const execs = [...(hist?.execucoes ?? [])].sort((a, b) => a.executadaEm.localeCompare(b.executadaEm));
  document.getElementById("qtdHistorico").textContent = execs.length || "";
  if (!execs.length) {
    alvo.replaceChildren(el("div", { class: "cartao" }, el("p", {}, "Nenhuma simulação registrada ainda. Use o botão ", el("b", {}, "Gerar nova simulação otimizada"), " na aba Parâmetros.")));
    return;
  }
  const melhorIdx = execs.reduce((bi, e, i) => {
    const m = e.melhorDaRodada ?? e.melhorGeral, b = execs[bi].melhorDaRodada ?? execs[bi].melhorGeral;
    return pontos(m) > pontos(b) || (pontos(m) === pontos(b) && m.kmTotal < b.kmTotal) ? i : bi;
  }, 0);
  const melhor = execs[melhorIdx].melhorDaRodada ?? execs[melhorIdx].melhorGeral;
  const dm = dataHora(execs[melhorIdx].executadaEm);

  const grupos = new Map();
  execs.forEach((e, i) => {
    const d = dataHora(e.executadaEm);
    const chaveData = `${d.getFullYear()}-${doisDig(d.getMonth() + 1)}-${doisDig(d.getDate())}`;
    if (!grupos.has(chaveData)) grupos.set(chaveData, { rotulo: d.toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "2-digit", year: "numeric" }), horas: new Map() });
    const horas = grupos.get(chaveData).horas;
    const h = d.getHours();
    if (!horas.has(h)) horas.set(h, []);
    horas.get(h).push({ e, numero: i + 1 });
  });

  const datas = [...grupos.entries()].sort((a, b) => b[0].localeCompare(a[0]));
  alvo.replaceChildren(
    el("section", { class: "cartao" },
      el("h2", { style: "margin-top:0" }, "Histórico de simulações"),
      el("p", {},
        `${execs.length} execuç${execs.length === 1 ? "ão registrada" : "ões registradas"}. Melhor de todas: `,
        el("b", {}, `${melhor.atendidas} OS e ${melhor.vencidasAtendidas} vencidas em rota, com ${n(Math.round(melhor.kmTotal))} km`),
        `, na execução #${melhorIdx + 1} (${dm.toLocaleString("pt-BR")}).`),
      el("p", { class: "nota" }, "Cada execução do botão ou da linha de comando entra aqui, agrupada por data e hora. Clique numa execução para ver os parâmetros e o ranking dela."),
      graficoEvolucao(execs)),
    ...datas.map(([, g], di) => el("details", { class: "cartao grupo-data", ...(di === 0 ? { open: "" } : {}) },
      el("summary", {}, `${g.rotulo} · ${[...g.horas.values()].flat().length} execuç${[...g.horas.values()].flat().length === 1 ? "ão" : "ões"}`),
      [...g.horas.entries()].sort((a, b) => b[0] - a[0]).map(([h, lista]) => el("div", { class: "grupo-hora" },
        el("h4", {}, `${doisDig(h)}h · ${lista.length} execuç${lista.length === 1 ? "ão" : "ões"}`),
        [...lista].reverse().map(({ e, numero }) => cartaoExecucao(e, numero, numero === melhorIdx + 1, repeticao(execs, numero - 1))))))),
  );
};

const mostrarAba = () => {
  const aba = location.hash === "#historico" ? "historico" : "parametros";
  for (const a of document.querySelectorAll(".abas a")) a.classList.toggle("ativa", a.dataset.aba === aba);
  for (const p of document.querySelectorAll("main[data-painel]")) p.hidden = p.dataset.painel !== aba;
  document.getElementById("seletorDia").style.visibility = aba === "parametros" ? "visible" : "hidden";
};
window.addEventListener("hashchange", mostrarAba);

const renderLinks = (dia) => {
  const links = [];
  if (dia.relatorio) links.push(el("a", { href: `../${dia.relatorio}` }, "relatório do dia"));
  if (dia.parametrosDoc) links.push(el("a", { href: `../${dia.parametrosDoc}` }, "detalhes dos parâmetros"));
  document.getElementById("linksDocs").replaceChildren("Fonte: ", ...links.flatMap((l, i) => (i ? [" · ", l] : [l])));
};

const mostrarDia = async (dia) => {
  const main = document.getElementById("conteudo");
  main.replaceChildren(el("p", { class: "aviso" }, "Carregando…"));
  const [resultado, sim] = await Promise.all([carregar(dia.resultado), dia.simulacao ? carregar(dia.simulacao) : null]);
  main.replaceChildren(document.getElementById("tpl").content.cloneNode(true));
  document.getElementById("cenario").textContent = `${resultado.cenario} · gerado em ${new Date(resultado.geradoEm).toLocaleString("pt-BR")}`;
  renderKpis(resultado);
  renderGrupos(resultado);
  renderCustos(resultado);
  renderCorrelacoes(resultado);
  renderPenalties(resultado);
  renderRotas(resultado);
  if (sim) {
    renderResumo(sim, dia, resultado);
    renderOtimizacao(sim, dia);
    renderAjustes(sim, dia);
    renderSequencia(sim, dia, resultado);
    renderSimulacao(sim, dia, resultado);
  }
  renderLinks(dia);
  ligarBotaoOtimizacao(dia);
};

(async () => {
  try {
    const { dias } = await carregar("indice.json");
    const sel = document.getElementById("dia");
    sel.replaceChildren(...dias.map((d, i) => el("option", { value: i }, d.data)));
    sel.value = String(dias.length - 1);
    sel.onchange = () => mostrarDia(dias[Number(sel.value)]).catch(erro);
    mostrarAba();
    renderHistorico(await carregar("historico-simulacoes.json").catch(() => null));
    await mostrarDia(dias.at(-1));
  } catch (e) {
    erro(e);
  }
})();

function erro(e) {
  document.getElementById("conteudo").replaceChildren(
    el("p", { class: "aviso" }, `Não consegui carregar os dados (${e.message}). Rode node docs/poc/painel/gerar-dados.mjs e abra esta página de novo.`),
  );
}
