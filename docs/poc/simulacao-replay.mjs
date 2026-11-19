// Uso: node docs/poc/simulacao-replay.mjs [pastaDados] [arquivoSaida]
// Simula localmente, sem chamar a Route Optimization API, o lote do InputESS_*.json
// sob cenários fixos de parâmetros. Grava apenas agregados, sem coordenadas, IDs ou aprovadores.
// Uma busca anterior de otimizar-parametros.mjs gravada no mesmo arquivo é preservada.
import fs from "fs";
import path from "path";
import { criarSimulador } from "./simulador.mjs";

const dataDir = process.argv[2] ?? path.join("docs", "Dados de treinamento");
const outFile = process.argv[3] ?? path.join("docs", "poc", "simulacao.json");

const CENARIOS = [
  { nome: "atual", descricao: "Parâmetros de 19/11 e janelas como no request.", parametros: {} },
  { nome: "pesos_sugeridos", descricao: "PotenciaPesos 2, OsPrazoImportancia 6, soft max 20 km. Janelas como no request.", parametros: { pesos: { PotenciaPesos: 2, OsPrazoImportancia: 6 }, softMaxMetros: 20000 } },
  { nome: "janela_vencida", descricao: "Só a correção da janela da vencida (turno inteiro, soft end +4 h, 10/h, sem custo de visita).", parametros: { corrigirVencida: true } },
  { nome: "sugerido_completo", descricao: "Pesos sugeridos, soft max 20 km e janela da vencida corrigida.", parametros: { pesos: { PotenciaPesos: 2, OsPrazoImportancia: 6 }, softMaxMetros: 20000, corrigirVencida: true } },
  { nome: "prazo6_potencia5", descricao: "Só OsPrazoImportancia 6, mantendo PotenciaPesos 5, soft max 1 km e janelas do request.", parametros: { pesos: { OsPrazoImportancia: 6 } } },
  { nome: "soft20km", descricao: "Só soft max 20 km.", parametros: { softMaxMetros: 20000 } },
  { nome: "prazo6_soft20", descricao: "PotenciaPesos 5 mantido, OsPrazoImportancia 6 e soft max 20 km. Janelas como no request.", parametros: { pesos: { OsPrazoImportancia: 6 }, softMaxMetros: 20000 } },
  { nome: "revisado", descricao: "PotenciaPesos 5 mantido, OsPrazoImportancia 6, soft max 20 km e janela da vencida corrigida.", parametros: { pesos: { OsPrazoImportancia: 6 }, softMaxMetros: 20000, corrigirVencida: true } },
];

const sim = criarSimulador(dataDir);
const anterior = fs.existsSync(outFile) ? JSON.parse(fs.readFileSync(outFile, "utf8")) : null;

const resultado = {
  cenario: sim.cenario,
  geradoEm: new Date().toISOString(),
  aviso: "Simulação local com heurística de inserção gulosa. Compare cenários entre si; o cenário 'atual' mostra a distância para a resposta real da Google.",
  calibracao: sim.calibracao,
  cenarios: {},
};

for (const c of CENARIOS) {
  const inicio = Date.now();
  resultado.cenarios[c.nome] = { descricao: c.descricao, parametros: c.parametros, ...sim.simular(c.parametros) };
  const r = resultado.cenarios[c.nome];
  console.log(`${c.nome}: ${r.atendidas} atendidas, ${r.vencidasAtendidas} vencidas, ${r.kmTotal} km, regras ${r.regras.ok ? "ok" : "violadas"}, ${((Date.now() - inicio) / 1000).toFixed(1)} s`);
}

if (anterior?.otimizacao) {
  resultado.otimizacao = anterior.otimizacao;
  if (anterior.cenarios?.otimizado) resultado.cenarios.otimizado = anterior.cenarios.otimizado;
}

fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, JSON.stringify(resultado, null, 2) + "\n", "utf8");
console.log(`Resultado gravado em ${outFile}`);
