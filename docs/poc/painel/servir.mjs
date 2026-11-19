// Uso: node docs/poc/painel/servir.mjs [porta]
// Serve docs/poc localmente e expõe a busca de parâmetros para o botão do painel.
// Os dados brutos são lidos só no servidor, a partir de dadosBrutos em indice.json; nada sai da máquina.
import http from "http";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { otimizar } from "../otimizar-parametros.mjs";
import { gerarDados } from "./gerar-dados.mjs";

const raiz = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const porta = Number(process.argv[2] ?? 5173);
const tipos = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css", ".json": "application/json", ".md": "text/markdown" };

let tarefa = { rodando: false };

const json = (res, status, corpo) => {
  res.writeHead(status, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
  res.end(JSON.stringify(corpo));
};

const iniciarOtimizacao = (data, avaliacoes) => {
  const indice = JSON.parse(fs.readFileSync(path.join(raiz, "indice.json"), "utf8"));
  const dia = indice.dias.find((d) => d.data === data) ?? indice.dias.at(-1);
  if (!dia?.dadosBrutos || !dia.simulacao) throw new Error(`Dia ${data} sem dadosBrutos ou simulacao em indice.json`);
  const { pasta, ...arquivos } = dia.dadosBrutos;
  tarefa = { rodando: true, dia: dia.data, iniciadaEm: new Date().toISOString(), progresso: { feitas: 0, total: avaliacoes } };
  otimizar({
    dataDir: path.resolve(raiz, pasta),
    arquivos,
    arquivoSimulacao: path.join(raiz, dia.simulacao),
    avaliacoes,
    dia: dia.data,
    origem: "painel",
    aoProgresso: (p) => { tarefa.progresso = p; },
  })
    .then((r) => {
      gerarDados();
      tarefa = { rodando: false, dia: dia.data, concluidaEm: new Date().toISOString(), resultado: { melhorou: r.melhorou, rodadas: r.rodadas, melhor: r.melhor } };
    })
    .catch((e) => {
      tarefa = { rodando: false, dia: dia.data, erro: e.message };
    });
};

http
  .createServer((req, res) => {
    const url = new URL(req.url, "http://localhost");
    const caminho = decodeURIComponent(url.pathname);

    if (caminho === "/api/otimizar") {
      if (req.method === "GET") return json(res, 200, tarefa);
      if (req.method === "POST") {
        if (tarefa.rodando) return json(res, 409, { erro: "Já existe uma busca em andamento.", ...tarefa });
        try {
          iniciarOtimizacao(url.searchParams.get("dia"), Math.min(400, Number(url.searchParams.get("avaliacoes") ?? 120)));
          return json(res, 202, tarefa);
        } catch (e) {
          return json(res, 400, { erro: e.message });
        }
      }
      return json(res, 405, { erro: "Use GET ou POST." });
    }

    if (caminho === "/") {
      res.writeHead(302, { Location: "/painel/" });
      return res.end();
    }
    let arquivo = path.join(raiz, caminho);
    if (!arquivo.startsWith(raiz)) {
      res.writeHead(403);
      return res.end();
    }
    if (caminho.endsWith("/")) arquivo = path.join(arquivo, "index.html");
    fs.readFile(arquivo, (erro, conteudo) => {
      if (erro) {
        res.writeHead(404);
        return res.end("Não encontrado");
      }
      res.writeHead(200, { "Content-Type": `${tipos[path.extname(arquivo)] ?? "application/octet-stream"}; charset=utf-8`, "Cache-Control": "no-store" });
      res.end(conteudo);
    });
  })
  .listen(porta, "127.0.0.1", () => console.log(`Painel em http://localhost:${porta}/painel/`));
