ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD HH24:MI:SS';
ALTER SESSION SET NLS_TIMESTAMP_FORMAT = 'YYYY-MM-DD HH24:MI:SS';

WITH emp AS (
    SELECT valor_long AS cod_empresa
      FROM cd_parametros_globais
     WHERE seq_par = 1
       AND cod_empresa = 0
       AND sistema = 'COMUM'
),
func_eq AS (
    SELECT
        ef.tip_equipe,
        ef.equipe,
        COUNT(1) AS tot_func
    FROM equipe_func ef
    GROUP BY
        ef.tip_equipe,
        ef.equipe
),
escala AS (
    SELECT
        iem.tip_equipe,
        iem.equipe,
        em.codpst_ope,
        MIN(iem.dthini_tur) + (15 / 1440) AS dthini_tur,
        MAX(iem.dthfim_tur) AS dthfim_tur,
        MAX(
            CASE
                WHEN tep.horini_rfc IS NOT NULL
                THEN TRUNC(iem.dthini_tur) + (tep.horini_rfc - TRUNC(tep.horini_rfc))
            END
        ) AS inicio_refeicao_real,
        MAX(
            CASE
                WHEN tep.horfim_rfc IS NOT NULL
                THEN TRUNC(iem.dthini_tur) + (tep.horfim_rfc - TRUNC(tep.horfim_rfc))
            END
        ) AS fim_refeicao_real
    FROM escala_mensal em
    INNER JOIN item_escala_mensal iem
            ON em.codpst_ope = iem.codpst_ope
           AND em.anoref = iem.anoref
           AND em.mesref = iem.mesref
    INNER JOIN turno_escala_padrao tep
            ON iem.codecl_pad = tep.codecl_pad
           AND iem.numseq_tur = tep.numseq_tur
    WHERE 
      iem.dthini_tur BETWEEN TRUNC(SYSDATE) + INTERVAL '5' HOUR AND TRUNC(SYSDATE) + INTERVAL '11' HOUR
      --AND em.idtsit < 2
      AND iem.indsit IN (1, 2)
    GROUP BY
        iem.tip_equipe,
        iem.equipe,
        em.codpst_ope
),
resp AS (
    SELECT
        eq.tip_equipe,
        eq.equipe,
        ef_resp.nummtr_fnc
    FROM equipe eq
    INNER JOIN equipe_func ef_resp
            ON eq.tip_equipe = ef_resp.tip_equipe
           AND eq.equipe = ef_resp.equipe
           AND ef_resp.fnc_resp = 1
    INNER JOIN pda p
            ON ef_resp.nummtr_fnc = p.nummtr_fnc
    WHERE eq.indeqp_atv = 1
      AND eq.situacao IN (1, 2)
      --AND eq.indeqp_epe = 'N'
      --AND p.indtip_cel <> 'I'
),
reg AS (
    SELECT
        er.tip_equipe,
        er.equipe,
        LISTAGG(emp.cod_empresa || '_' || er.codregiao, ',')
            WITHIN GROUP (ORDER BY er.codregiao) AS regioes_equipe
    FROM equipe_regiao er
    CROSS JOIN emp
    GROUP BY
        er.tip_equipe,
        er.equipe
),
coord_base AS (
    SELECT
        esc.tip_equipe,
        esc.equipe,
        po.vlr_coordenada_latitude  AS latitude_base,
        po.vlr_coordenada_longitude AS longitude_base
    FROM escala esc
    INNER JOIN posto_operacional po
            ON po.codpst_ope = esc.codpst_ope
    WHERE po.vlr_coordenada_latitude  IS NOT NULL
      AND po.vlr_coordenada_longitude IS NOT NULL
      AND po.vlr_coordenada_latitude  <> 0
      AND po.vlr_coordenada_longitude <> 0
      AND po.vlr_coordenada_latitude  BETWEEN -34 AND 6
      AND po.vlr_coordenada_longitude BETWEEN -74 AND -28
),
desp_regiao AS (
    SELECT DISTINCT
        b.codreg,
        b.codpolo,
        b.codregiao
    FROM despachante_regiao b
    INNER JOIN despachante_regiao_escala e
            ON e.sgl_despacho = b.sgl_despacho
           AND b.codreg = e.cod_regional
           AND b.codpolo = e.cod_polo
           AND b.codregiao = e.cod_regiao
),
eq_habilitada_despacho AS (
    SELECT DISTINCT
        er.tip_equipe,
        er.equipe
    FROM equipe_regiao er
    WHERE
           (er.codreg, er.codpolo, er.codregiao) IN (
                SELECT dr.codreg, dr.codpolo, dr.codregiao
                  FROM desp_regiao dr
                 WHERE dr.codregiao > 0
                   AND dr.codpolo > 0
           )
        OR (er.codreg, er.codpolo) IN (
                SELECT dr.codreg, dr.codpolo
                  FROM desp_regiao dr
                 WHERE dr.codregiao = 0
                   AND dr.codpolo > 0
           )
        OR er.codreg IN (
                SELECT dr.codreg
                  FROM desp_regiao dr
                 WHERE dr.codregiao = 0
                   AND dr.codpolo = 0
           )
),
-- Regiões fixas por prefixo de equipe (sobrepõe o cadastro quando há match)
reg_prefixo AS (
    SELECT
        eq.tip_equipe,
        eq.equipe,
        CASE SUBSTR(eq.equipe, 1, 3)
            WHEN 'CGA' THEN emp.cod_empresa || '_102,' || emp.cod_empresa || '_103'
            WHEN 'CGB' THEN emp.cod_empresa || '_57,'  || emp.cod_empresa || '_58'
            WHEN 'CGM' THEN emp.cod_empresa || '_59,'  || emp.cod_empresa || '_60'
            WHEN 'CGO' THEN emp.cod_empresa || '_100,' || emp.cod_empresa || '_101'
        END AS regioes_prefixo
    FROM equipe eq
    CROSS JOIN emp
    WHERE SUBSTR(eq.equipe, 1, 3) IN ('CGA', 'CGB', 'CGM', 'CGO')
)
SELECT
    eq.equipe                                                        AS EQUIPE,
    eq.tip_equipe                                                    AS TIPO_EQUIPE,
    'U/R'                                                            AS ATUACAO_EQUIPE,

    cb.latitude_base                                                 AS LATITUDE_LOCAL_PARTIDA,
    cb.longitude_base                                                AS LONGITUDE_LOCAL_PARTIDA,
    TO_NUMBER(NULL)                                                AS LATITUDE_LOCAL_RETORNO,
    TO_NUMBER(NULL)                                               AS LONGITUDE_LOCAL_RETORNO,

    70                                                               AS PORCENTAGEM_CARGA_TRABALHO,
    func_eq.tot_func                                                 AS N_PESSOAS,
    1                                                  AS CUSTO_FIXO,
    3                                                             AS CUSTO_KM,

    TO_CHAR(escala.dthini_tur,           'YYYY-MM-DD HH24:MI:SS')   AS INICIO_TURNO,
    TO_CHAR(escala.inicio_refeicao_real, 'YYYY-MM-DD HH24:MI:SS')   AS INICIO_REFEICAO,
    TO_CHAR(escala.fim_refeicao_real,    'YYYY-MM-DD HH24:MI:SS')   AS FIM_REFEICAO,
    TO_CHAR(escala.dthfim_tur,           'YYYY-MM-DD HH24:MI:SS')   AS FIM_TURNO,

    CASE
        WHEN escala.inicio_refeicao_real IS NOT NULL
         AND escala.fim_refeicao_real    IS NOT NULL
        THEN ROUND((escala.fim_refeicao_real - escala.inicio_refeicao_real) * 1440)
    END                                                              AS TEMPO_REFEICAO,

    TO_NUMBER(NULL)                                                 AS LATITUDE_LOCAL_REFEICAO,
    TO_NUMBER(NULL)                                                 AS LONGITUDE_LOCAL_REFEICAO,

    60                                                              AS MAX_LOAD,
    pe.dsc_perfil                                                    AS PERFIL_EQUIPE,

    -- Prefixo reconhecido → regiões fixas; demais → cadastro normal
    COALESCE(rp.regioes_prefixo, reg.regioes_equipe)                AS REGIOES_EQUIPE

FROM equipe eq
INNER JOIN func_eq
        ON eq.tip_equipe = func_eq.tip_equipe
       AND eq.equipe     = func_eq.equipe
INNER JOIN escala
        ON eq.tip_equipe = escala.tip_equipe
       AND eq.equipe     = escala.equipe
INNER JOIN resp
        ON eq.tip_equipe = resp.tip_equipe
       AND eq.equipe     = resp.equipe
INNER JOIN coord_base cb
        ON eq.tip_equipe = cb.tip_equipe
       AND eq.equipe     = cb.equipe
INNER JOIN eq_habilitada_despacho ehd
        ON eq.tip_equipe = ehd.tip_equipe
       AND eq.equipe     = ehd.equipe
LEFT JOIN reg
       ON eq.tip_equipe  = reg.tip_equipe
      AND eq.equipe      = reg.equipe
LEFT JOIN perfil_equipes pe
       ON eq.cod_perfil  = pe.cod_perfil
LEFT JOIN reg_prefixo rp
       ON eq.tip_equipe  = rp.tip_equipe
      AND eq.equipe      = rp.equipe
WHERE eq.indeqp_atv = 1
  AND eq.situacao   IN (1, 2)
  --AND eq.indeqp_epe = 'N'
  AND cb.latitude_base   IS NOT NULL
  AND cb.longitude_base  IS NOT NULL
--  AND eq.cod_perfil IN (2, 3, 202, 203)
  AND (
        (escala.dthfim_tur - escala.dthini_tur) * 24
        - CASE
              WHEN escala.inicio_refeicao_real IS NOT NULL
               AND escala.fim_refeicao_real    IS NOT NULL
              THEN (escala.fim_refeicao_real - escala.inicio_refeicao_real) * 24
              ELSE 0
          END
      ) <= 10