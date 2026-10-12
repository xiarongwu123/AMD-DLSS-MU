import { useEffect, useState, useRef } from "react";
import { ThumbsUp, MessageSquare, ImagePlus, X } from "lucide-react";
import { rpc } from "./bridge";
import { Button, Loading, Empty, Modal } from "./components";
import type { WishList, WishDetail, Wish } from "./types";
const labels: Record<string, string> = {
  all: "全部",
  hot: "热门",
  planned: "已计划",
  developing: "开发中",
  completed: "已完成",
  mine: "我的愿望",
  new: "待评估",
};
export function WishesPage({ notify }: { notify: (text: string) => void }) {
  const [list, setList] = useState<WishList | null>(null),
    [filter, setFilter] = useState("all"),
    [sort, setSort] = useState("newest"),
    [query, setQuery] = useState(""),
    [page, setPage] = useState(1),
    [content, setContent] = useState(""),
    [image, setImage] = useState(""),
    [detail, setDetail] = useState<WishDetail | null>(null),
    [comment, setComment] = useState(""),
    [loading, setLoading] = useState(false),
    [submitting, setSubmitting] = useState(false),
    [error, setError] = useState(""),
    [revision, setRevision] = useState(0);
  const submissionId = useRef(crypto.randomUUID());
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    const timer = setTimeout(
      () =>
        rpc<WishList>("wishes", { filter, query, sort, page })
          .then((v) => {
            if (active) setList(v);
          })
          .catch((e) => active && setError(e.message))
          .finally(() => active && setLoading(false)),
      250,
    );
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [filter, query, sort, page, revision]);
  const act = async (fn: () => Promise<void>) => {
    setSubmitting(true);
    setError("");
    try {
      await fn();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };
  const vote = (wish: Wish) =>
    act(async () => {
      await rpc("voteWish", { id: wish.id, voted: !wish.voted });
      setRevision((r) => r + 1);
      if (detail?.wish.id === wish.id)
        setDetail(await rpc<WishDetail>("wishDetail", { id: wish.id }));
    });
  async function attach(file?: File) {
    if (!file) return;
    if (
      !["image/png", "image/jpeg"].includes(file.type) ||
      file.size > 10 * 1024 * 1024
    ) {
      setError("请选择 10 MB 以内的 PNG 或 JPG 图片。");
      return;
    }
    try {
      const bitmap = await createImageBitmap(file);
      if (bitmap.width * bitmap.height > 40000000) {
        bitmap.close();
        throw new Error("图片尺寸过大。");
      }
      const canvas = document.createElement("canvas");
      const scale = Math.min(1, 960 / Math.max(bitmap.width, bitmap.height));
      canvas.width = Math.round(bitmap.width * scale);
      canvas.height = Math.round(bitmap.height * scale);
      canvas
        .getContext("2d")!
        .drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      bitmap.close();
      const data = canvas.toDataURL("image/jpeg", 0.75);
      if (data.length > 334000)
        throw new Error("图片压缩后仍过大，请选择较简单的图片。");
      setImage(data);
      submissionId.current = crypto.randomUUID();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  return (
    <div className="wish-page">
      <section className="wish-hero">
        <h1>许愿池</h1>
        <p>你想要的功能，在这里告诉我们。</p>
      </section>
      <section className="panel composer">
        <h3>投入一个愿望</h3>
        <textarea
          aria-label="愿望内容"
          value={content}
          onChange={(e) => {
            setContent(e.target.value);
            submissionId.current = crypto.randomUUID();
          }}
          maxLength={500}
          placeholder="希望 MU 增加什么？"
        />
        {image && (
          <div className="attachment">
            <img src={image} alt="愿望附图" />
            <button
              aria-label="移除附图"
              className="icon-button"
              onClick={() => {
                setImage("");
                submissionId.current = crypto.randomUUID();
              }}
            >
              <X />
            </button>
          </div>
        )}
        <div className="composer-footer">
          <label className="button">
            <ImagePlus size={18} />
            添加图片
            <input
              type="file"
              accept="image/png,image/jpeg"
              hidden
              onChange={(e) => {
                void attach(e.target.files?.[0]);
                e.target.value = "";
              }}
            />
          </label>
          <span className="muted">{content.length}/500</span>
          <Button
            primary
            disabled={submitting || content.trim().length < 5}
            onClick={() =>
              act(async () => {
                await rpc("submitWish", {
                  content: content.trim(),
                  imageData: image,
                  submissionId: submissionId.current,
                });
                setContent("");
                setImage("");
                submissionId.current = crypto.randomUUID();
                setFilter("mine");
                setPage(1);
                setRevision((r) => r + 1);
                notify("愿望已提交");
              })
            }
          >
            提交愿望
          </Button>
        </div>
      </section>
      <div className="wish-toolbar">
        <div className="tabs">
          {Object.keys(labels)
            .filter((x) => x !== "new")
            .map((key) => (
              <button
                key={key}
                className={filter === key ? "active" : ""}
                onClick={() => {
                  setFilter(key);
                  setPage(1);
                }}
              >
                {labels[key]} {list?.counts[key] || ""}
              </button>
            ))}
        </div>
        <input
          aria-label="搜索愿望"
          placeholder="搜索愿望"
          maxLength={100}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setPage(1);
          }}
        />
        <select
          aria-label="愿望排序"
          value={sort}
          onChange={(e) => {
            setSort(e.target.value);
            setPage(1);
          }}
        >
          <option value="newest">最新</option>
          <option value="votes">最多支持</option>
        </select>
      </div>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {loading ? (
        <Loading text="正在读取许愿池…" />
      ) : !list?.items.length ? (
        <Empty title="这里还没有愿望">写下你的建议，让 MU 更好用。</Empty>
      ) : (
        <div className="wish-list">
          {list.items.map((w) => (
            <article key={w.id} className="panel wish-row">
              <div>
                <span className="badge">{labels[w.status] || w.status}</span>
                {w.isDemo && <span className="muted"> 展示建议</span>}
                <button
                  className="wish-title"
                  onClick={() =>
                    act(async () =>
                      setDetail(
                        await rpc<WishDetail>("wishDetail", { id: w.id }),
                      ),
                    )
                  }
                >
                  {w.title}
                </button>
                <p>{w.content}</p>
                <small className="muted">
                  {w.author} ·{" "}
                  {new Date(w.createdAt * 1000).toLocaleDateString()}
                </small>
              </div>
              <div className="actions">
                <Button
                  aria-pressed={w.voted}
                  disabled={submitting}
                  onClick={() => vote(w)}
                >
                  <ThumbsUp size={17} />
                  {w.votes}
                </Button>
                <Button
                  onClick={() =>
                    act(async () =>
                      setDetail(
                        await rpc<WishDetail>("wishDetail", { id: w.id }),
                      ),
                    )
                  }
                >
                  <MessageSquare size={17} />
                  {w.comments}
                </Button>
              </div>
            </article>
          ))}
        </div>
      )}
      <div className="pagination">
        <Button
          disabled={page <= 1 || loading}
          onClick={() => setPage((p) => p - 1)}
        >
          上一页
        </Button>
        <span>第 {page} 页</span>
        <Button
          disabled={!list || page * list.pageSize >= list.total || loading}
          onClick={() => setPage((p) => p + 1)}
        >
          下一页
        </Button>
      </div>
      {detail && (
        <Modal title="愿望详情" onClose={() => setDetail(null)}>
          <h3>{detail.wish.title}</h3>
          <p className="preserve">{detail.wish.content}</p>
          {detail.imageData && (
            <img
              className="wish-detail-image"
              src={detail.imageData}
              alt="愿望附图"
            />
          )}
          <Button disabled={submitting} onClick={() => vote(detail.wish)}>
            <ThumbsUp size={17} />
            {detail.wish.voted ? "已支持" : "支持"} · {detail.wish.votes}
          </Button>
          <h4>讨论</h4>
          {detail.comments.map((c) => (
            <div className="comment" key={c.id}>
              <strong>{c.author}</strong>
              <p>{c.content}</p>
            </div>
          ))}
          <textarea
            aria-label="评论内容"
            placeholder="写下你的想法"
            value={comment}
            maxLength={500}
            onChange={(e) => setComment(e.target.value)}
          />
          <Button
            primary
            disabled={submitting || !comment.trim()}
            onClick={() =>
              act(async () => {
                await rpc("commentWish", {
                  id: detail.wish.id,
                  content: comment.trim(),
                });
                setComment("");
                setDetail(
                  await rpc<WishDetail>("wishDetail", { id: detail.wish.id }),
                );
                setRevision((r) => r + 1);
              })
            }
          >
            发布评论
          </Button>
          {error && <p className="error">{error}</p>}
        </Modal>
      )}
    </div>
  );
}
