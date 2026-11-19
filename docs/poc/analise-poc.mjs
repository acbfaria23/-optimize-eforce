// Uso: node docs/poc/analise-poc.mjs [pastaDados] [arquivoSaida]
// Lê InputESS_*.json (request Google), OutputESS_*.json (resposta) e Executado.json (aprovação)
// e grava apenas métricas agregadas, sem coordenadas, IDs de OS ou aprovadores.
import fs from "fs";
import path from "path";

const dataDir = process.argv[2] ?? path.join("docs", "Dados de treinamento");
const outFile = process.argv[3] ?? path.join("docs", "poc", "resultado.json");

const pick = (prefix) => {
  const name = fs.readdirSync(dataDir).find((f) => f.startsWith(prefix) && f.endsWith(".json"));
  if (!name) throw new Error(`Arquivo ${prefix}*.json não encontrado em ${dataDir}`);
  return JSON.parse(fs.readFileSync(path.join(dataDir, name), "utf8"));
};

const input = pick("InputESS");
const output = pick("OutputESS");
const exec = pick("Executado")[0].ResultadoOtimizacao;

const num = (label, key) => {
  const m = label.match(new RegExp(key + "\\s*(-?[\\d.]+)"));
  return m ? Number(m[1]) : null;
};

const parseLabel = (label) => ({
  id: (label.match(/^[0-9a-f]+/i) || [""])[0],
  risco: /NAO RISCO/.test(label) ? "N" : /RISCO A VIDA/.test(label) ? "S" : "?",
  regulada: /NAO REGULADA/.test(label) ? "N" : /REGULADA/.test(label) ? "S" : "?",
  prazo: /VENC/.test(label) ? "VENCIDA" : /NO PRAZO/.test(label) ? "NO_PRAZO" : "?",
  tipo: (label.match(/COMERCIAL|TECNICA|LOTE/) || ["?"])[0],
  loc: (label.match(/Localizacao:\s*([A-Za-z]+)/) || [])[1] || "?",
  reinc: /Reincidente:\s*S/.test(label) ? "S" : "N",
  prio: (label.match(/Prioridade:\s*([A-Z]+)/) || [])[1] || "?",
  pessoas: num(label, "Afeta"),
  horasAberta: num(label, "Aberta a:"),
  compensacao: num(label, "Valor de Compensacao:"),
  compensacaoHora: num(label, "Valor de Compensacao Por Hora:"),
  horasAteVencer: num(label, "Tempo ate vencer:"),
});

const ships = input.model.shipments.map((s) => ({
  ...parseLabel(s.label),
  penalty: Number(s.penaltyCost),
  equipesPermitidas: (s.allowedVehicleIndices || []).length,
}));
const byId = new Map(ships.map((s) => [s.id, s]));

const codigo = (label) => String(label || "").split(",")[0];
const equipeAprovada = new Map(exec.Equipes.map((e) => [codigo(e.Equipe), !!e.Aprovada]));

const grupoPorId = new Map();
for (const e of exec.Equipes) {
  for (const os of e.OrdensServico) {
    if (!os.IsAlmoco) grupoPorId.set(os._id, e.Aprovada ? "aprovada" : "planejada_nao_aprovada");
  }
}
for (const os of exec.OsNaoAtendidas) grupoPorId.set(os._id, "fora_da_rota");

const grupos = ["aprovada", "planejada_nao_aprovada", "fora_da_rota"];
const rows = ships.map((s) => ({ ...s, grupo: grupoPorId.get(s.id) ?? "sem_correspondencia" }));

const round = (n) => Math.round(n * 10) / 10;
const quant = (arr) => {
  const s = arr.filter(Number.isFinite).sort((a, b) => a - b);
  if (!s.length) return null;
  const p = (k) => s[Math.min(s.length - 1, Math.floor(k * (s.length - 1)))];
  return { n: s.length, min: round(s[0]), p25: round(p(0.25)), p50: round(p(0.5)), p75: round(p(0.75)), max: round(s.at(-1)) };
};
const tally = (arr, fn) => arr.reduce((m, x) => ((m[fn(x)] = (m[fn(x)] || 0) + 1), m), {});
const corr = (xs, ys) => {
  const pares = xs.map((x, i) => [x, ys[i]]).filter(([x, y]) => Number.isFinite(x) && Number.isFinite(y));
  if (pares.length < 3) return null;
  const mx = pares.reduce((a, [x]) => a + x, 0) / pares.length;
  const my = pares.reduce((a, [, y]) => a + y, 0) / pares.length;
  let n = 0, dx = 0, dy = 0;
  for (const [x, y] of pares) {
    n += (x - mx) * (y - my);
    dx += (x - mx) ** 2;
    dy += (y - my) ** 2;
  }
  return dx && dy ? Math.round((n / Math.sqrt(dx * dy)) * 100) / 100 : null;
};

const rotas = output.routes.map((r) => {
  const visitas = (r.visits || []).filter((v) => !/REFEICAO|ALMOCO/i.test(v.shipmentLabel || "")).length;
  return {
    aprovada: equipeAprovada.get(codigo(r.vehicleLabel)) ?? false,
    visitas,
    km: (r.metrics?.travelDistanceMeters || 0) / 1000,
  };
});
const rotasCom = (aprovada) => rotas.filter((r) => r.aprovada === aprovada && r.visitas > 0);

const resultado = {
  cenario: input.label,
  geradoEm: new Date().toISOString(),
  totais: {
    osNoRequest: ships.length,
    veiculos: input.model.vehicles.length,
    veiculosUsados: output.metrics.usedVehicleCount,
    rotasSemVisita: rotas.filter((r) => r.visitas === 0).length,
    equipesAprovadas: exec.Equipes.filter((e) => e.Aprovada).length,
    porGrupo: tally(rows, (r) => r.grupo),
  },
  custosGoogle: output.metrics.costs,
  perfilDoLote: Object.fromEntries(
    ["risco", "regulada", "prazo", "tipo", "loc", "prio", "reinc"].map((k) => [k, tally(ships, (s) => s[k])]),
  ),
  vencidasPorGrupo: tally(rows.filter((r) => r.prazo === "VENCIDA"), (r) => r.grupo),
  porGrupo: Object.fromEntries(
    grupos.map((g) => {
      const a = rows.filter((r) => r.grupo === g);
      return [g, {
        penalty: quant(a.map((x) => x.penalty)),
        compensacao: quant(a.map((x) => x.compensacao)),
        compensacaoHora: quant(a.map((x) => x.compensacaoHora)),
        horasAberta: quant(a.map((x) => x.horasAberta)),
        horasAteVencer: quant(a.map((x) => x.horasAteVencer)),
        equipesPermitidas: quant(a.map((x) => x.equipesPermitidas)),
        prazo: tally(a, (x) => x.prazo),
        loc: tally(a, (x) => x.loc),
      }];
    }),
  ),
  correlacaoComPenalty: Object.fromEntries(
    ["compensacao", "compensacaoHora", "horasAberta", "horasAteVencer"].map((k) => [
      k,
      corr(ships.map((s) => s[k]), ships.map((s) => s.penalty)),
    ]).concat([["vencida", corr(ships.map((s) => (s.prazo === "VENCIDA" ? 1 : 0)), ships.map((s) => s.penalty))]]),
  ),
  rotas: {
    aprovadas: { km: quant(rotasCom(true).map((r) => r.km)), visitas: quant(rotasCom(true).map((r) => r.visitas)) },
    naoAprovadasComVisita: { km: quant(rotasCom(false).map((r) => r.km)), visitas: quant(rotasCom(false).map((r) => r.visitas)) },
  },
};

fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, JSON.stringify(resultado, null, 2) + "\n", "utf8");
console.log(`Resultado gravado em ${outFile}`);
